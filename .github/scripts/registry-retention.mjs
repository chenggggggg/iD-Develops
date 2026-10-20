import { appendFileSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const shaPattern = /^[a-f0-9]{40}$/;
const digestPattern = /^sha256:[a-f0-9]{64}$/;
const tagPattern = /^[\w][\w.-]{0,127}$/;
const day = 24 * 60 * 60 * 1000;
const rollbackCount = 3;
const graceDays = 7;
const batchSize = 20;

function requireValue(value, name) {
  if (!value) throw new Error(`${name} is required.`);
  return value;
}

function versionUrl(value) {
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.username || url.password || url.search || url.hash ||
      url.pathname !== '/health/version') {
    throw new Error('Protected version URLs must be HTTPS /health/version URLs without credentials or query strings.');
  }
  return url.href;
}

export function readConfig(env) {
  const mode = env.REGISTRY_RETENTION_MODE || 'report';
  if (!['report', 'apply'].includes(mode)) throw new Error('REGISTRY_RETENTION_MODE must be report or apply.');
  const registry = requireValue(env.DO_REGISTRY_NAME, 'DO_REGISTRY_NAME');
  const repository = requireValue(env.DO_IMAGE_REPOSITORY, 'DO_IMAGE_REPOSITORY');
  const githubRepository = requireValue(env.GITHUB_REPOSITORY, 'GITHUB_REPOSITORY');
  if (!/^[a-z0-9][a-z0-9-]*$/.test(registry) ||
      !/^[a-z0-9][a-z0-9._/-]*$/.test(repository) ||
      !/^[\w.-]+\/[\w.-]+$/.test(githubRepository)) {
    throw new Error('Invalid registry or repository configuration.');
  }
  const currentSha = requireValue(env.GITHUB_SHA, 'GITHUB_SHA');
  if (!shaPattern.test(currentSha)) throw new Error('GITHUB_SHA must be a full commit hash.');
  const stagingUrl = versionUrl(`${requireValue(env.STAGING_URL, 'STAGING_URL').replace(/\/$/, '')}/health/version`);
  const inventoryConfigured = Boolean(env.REGISTRY_RETENTION_VERSION_URLS?.trim());
  const configuredUrls = inventoryConfigured ? JSON.parse(env.REGISTRY_RETENTION_VERSION_URLS) : [];
  if (!Array.isArray(configuredUrls) || (inventoryConfigured && configuredUrls.length === 0) ||
      configuredUrls.some(value => typeof value !== 'string')) {
    throw new Error('REGISTRY_RETENTION_VERSION_URLS must be a nonempty JSON array of version URLs.');
  }
  if (mode === 'apply' && (!inventoryConfigured ||
      env.GITHUB_REF !== 'refs/heads/main' || !['push', 'workflow_dispatch'].includes(env.GITHUB_EVENT_NAME))) {
    throw new Error('Apply requires an explicit environment inventory and a main-branch push or manual deployment.');
  }
  return {
    mode, registry, repository, githubRepository, currentSha, stagingUrl, inventoryConfigured,
    versionUrls: [...new Set([stagingUrl, ...configuredUrls.map(versionUrl)])],
    digitalOceanToken: requireValue(env.DIGITALOCEAN_ACCESS_TOKEN, 'DIGITALOCEAN_ACCESS_TOKEN'),
    githubToken: requireValue(env.GITHUB_TOKEN, 'GITHUB_TOKEN'),
  };
}

function normalizeManifests(manifests) {
  const digests = new Set();
  const tags = new Set();
  return manifests.map(manifest => {
    const imageTags = manifest.tags ?? [];
    if (!digestPattern.test(manifest.digest) || digests.has(manifest.digest) ||
        !Array.isArray(imageTags) || imageTags.some(tag => typeof tag !== 'string' || !tagPattern.test(tag)) ||
        !Number.isFinite(Date.parse(manifest.updated_at))) {
      throw new Error('Registry returned invalid or duplicate manifest metadata; cleanup stopped.');
    }
    digests.add(manifest.digest);
    for (const tag of imageTags) {
      if (tags.has(tag)) throw new Error('Registry tags changed during pagination; cleanup stopped.');
      tags.add(tag);
    }
    return { digest: manifest.digest, tags: [...imageTags].sort(), updatedAt: manifest.updated_at };
  });
}

export function buildPlan(manifests, { currentSha, deployedVersions, successfulVersions, now }) {
  const images = normalizeManifests(manifests);
  const byTag = new Map(images.flatMap(image => image.tags.map(tag => [tag, image])));
  const protectedDigests = new Set();
  for (const version of [currentSha, ...deployedVersions]) {
    if (!shaPattern.test(version) || !byTag.has(version)) {
      throw new Error('A deployed version cannot be resolved to an immutable registry tag; cleanup stopped.');
    }
    protectedDigests.add(byTag.get(version).digest);
  }

  const rollbackDigests = new Set();
  for (const version of successfulVersions) {
    const image = byTag.get(version);
    if (image && !image.tags.includes(currentSha)) rollbackDigests.add(image.digest);
    if (rollbackDigests.size === rollbackCount) break;
  }

  let candidates = 0;
  return images.sort((a, b) => Date.parse(a.updatedAt) - Date.parse(b.updatedAt)).map(image => {
    const reasons = [];
    if (protectedDigests.has(image.digest)) reasons.push('currently deployed');
    if (rollbackDigests.has(image.digest)) reasons.push('successful rollback version');
    if (image.tags.some(tag => !shaPattern.test(tag))) reasons.push('named tag (environment, release, or manual pin)');
    if (image.tags.length === 0) reasons.push('untagged image: ownership unknown');
    if (Date.parse(image.updatedAt) >= now.getTime() - graceDays * day) reasons.push('within seven-day grace period');
    if (reasons.length) return { ...image, action: 'keep', reasons };
    candidates += 1;
    return { ...image, action: candidates <= batchSize ? 'delete' : 'defer', reasons: ['obsolete commit image', ...(candidates > batchSize ? ['twenty-image batch limit'] : [])] };
  });
}

async function request(fetchImpl, url, token, method = 'GET') {
  const response = await fetchImpl(url, {
    method, redirect: 'error', signal: AbortSignal.timeout(30_000),
    headers: token ? { Authorization: `Bearer ${token}`, Accept: 'application/json' } : { Accept: 'application/json' },
  });
  // Do not print response bodies: provider errors can contain sensitive configuration.
  if (!response.ok) throw new Error(`${method} ${new URL(url).host} returned HTTP ${response.status}; cleanup stopped.`);
  if (method === 'DELETE') {
    if (response.status !== 204) throw new Error('Registry did not confirm manifest deletion.');
    return null;
  }
  return response.json();
}

function manifestsUrl(config) {
  return `https://api.digitalocean.com/v2/registries/${encodeURIComponent(config.registry)}/repositories/${encodeURIComponent(config.repository)}/digests`;
}

async function listManifests(config, fetchImpl) {
  const manifests = [];
  for (let page = 1; page <= 100; page += 1) {
    const data = await request(fetchImpl, `${manifestsUrl(config)}?per_page=100&page=${page}`, config.digitalOceanToken);
    if (!Array.isArray(data.manifests)) throw new Error('Registry manifest response is incomplete.');
    manifests.push(...data.manifests);
    if (!data.links?.pages?.next && data.manifests.length < 100) {
      if (data.meta?.total !== undefined && data.meta.total !== manifests.length) {
        throw new Error('Registry inventory changed or pagination is incomplete; cleanup stopped.');
      }
      normalizeManifests(manifests);
      return manifests;
    }
  }
  throw new Error('Registry inventory exceeds the pagination limit; cleanup stopped.');
}

async function deployedVersions(config, fetchImpl) {
  const versions = [];
  for (const url of config.versionUrls) {
    // Public health requests never receive the registry or GitHub credentials.
    const data = await request(fetchImpl, url);
    if (!shaPattern.test(data.version) || (url === config.stagingUrl && data.version !== config.currentSha)) {
      throw new Error('An environment version is unknown or staging has changed; cleanup stopped.');
    }
    versions.push(data.version);
  }
  return versions;
}

async function successfulVersions(config, manifests, fetchImpl) {
  const byTag = new Map(manifests.flatMap(image => (image.tags ?? []).map(tag => [tag, image.digest])));
  const available = new Set();
  const versions = [];
  for (let page = 1; page <= 10; page += 1) {
    const url = `https://api.github.com/repos/${config.githubRepository}/actions/workflows/ci-cd.yml/runs?branch=main&status=success&per_page=100&page=${page}`;
    const data = await request(fetchImpl, url, config.githubToken);
    if (!Array.isArray(data.workflow_runs)) throw new Error('Successful deployment history is unavailable.');
    for (const run of data.workflow_runs) {
      if (run.conclusion === 'success' && run.head_branch === 'main' &&
          ['push', 'workflow_dispatch'].includes(run.event) && shaPattern.test(run.head_sha)) {
        versions.push(run.head_sha);
        const digest = byTag.get(run.head_sha);
        if (digest && digest !== byTag.get(config.currentSha)) available.add(digest);
      }
    }
    if (available.size >= rollbackCount || data.workflow_runs.length < 100) return versions;
  }
  throw new Error('Cannot establish rollback history within the GitHub search limit; cleanup stopped.');
}

function fingerprint(manifests, versions) {
  const images = normalizeManifests(manifests).sort((a, b) => a.digest.localeCompare(b.digest));
  return JSON.stringify({ images, versions });
}

export function renderReport(report) {
  const lines = [
    '## Container image retention', '',
    `Mode: **${report.mode}**. Keep deployed images, three available successful rollback versions, named tags, and images updated within seven days. Delete at most twenty obsolete commit images per run.`, '',
  ];
  if (!report.inventoryConfigured) {
    lines.push('Report uses staging only. Before enabling deletion, configure REGISTRY_RETENTION_VERSION_URLS with every environment using this repository.', '');
  }
  if (report.error) lines.push(`Cleanup stopped: ${report.error}`, '');
  lines.push('| Digest | Tags | Updated (UTC) | Decision | Reason |', '| --- | --- | --- | --- | --- |');
  for (const image of report.images) {
    const decision = image.action === 'delete' ? (report.mode === 'report' ? 'Would delete' : 'Not deleted') : image.action;
    lines.push(`| ${image.digest} | ${image.tags.join(', ') || '(none)'} | ${image.updatedAt} | ${decision} | ${image.reasons.join('; ')} |`);
  }
  lines.push('', 'Deleting a manifest makes only its unreferenced layers eligible for garbage collection; shared layers remain. This workflow does not run registry-wide garbage collection.', '');
  return lines.join('\n');
}

export async function runRetention(config, { fetchImpl = fetch, now = new Date(), saveReport = () => {} } = {}) {
  const report = { mode: config.mode, inventoryConfigured: config.inventoryConfigured, images: [] };
  try {
    const manifests = await listManifests(config, fetchImpl);
    const versions = await deployedVersions(config, fetchImpl);
    const history = await successfulVersions(config, manifests, fetchImpl);
    report.images = buildPlan(manifests, { currentSha: config.currentSha, deployedVersions: versions, successfulVersions: history, now });
    saveReport(report);

    if (config.mode === 'apply' && report.images.some(image => image.action === 'delete')) {
      // Recheck all environments and the full registry inventory before each deletion.
      // Other writers must share the deployment concurrency lock (see the runbook).
      const remaining = [...manifests];
      for (const image of report.images.filter(image => image.action === 'delete')) {
        const fresh = await listManifests(config, fetchImpl);
        const liveVersions = await deployedVersions(config, fetchImpl);
        if (fingerprint(fresh, liveVersions) !== fingerprint(remaining, versions)) {
          throw new Error('Registry inventory or deployed versions changed after planning; cleanup stopped.');
        }
        await request(fetchImpl, `${manifestsUrl(config)}/${encodeURIComponent(image.digest)}`, config.digitalOceanToken, 'DELETE');
        image.action = 'deleted';
        remaining.splice(remaining.findIndex(manifest => manifest.digest === image.digest), 1);
        saveReport(report);
      }
    }
    return report;
  } catch (error) {
    report.error = error.message;
    throw error;
  } finally {
    saveReport(report);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const reportPath = resolve(process.env.REGISTRY_RETENTION_REPORT_PATH || 'registry-retention-report');
  const saveReport = report => {
    mkdirSync(dirname(reportPath), { recursive: true });
    writeFileSync(`${reportPath}.json`, `${JSON.stringify(report, null, 2)}\n`);
    writeFileSync(`${reportPath}.md`, renderReport(report));
  };
  try {
    const report = await runRetention(readConfig(process.env), { saveReport });
    if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, renderReport(report));
    console.log(`Retention ${report.mode} complete: ${report.images.filter(image => image.action === 'delete').length} candidates, ${report.images.filter(image => image.action === 'deleted').length} deleted.`);
  } catch (error) {
    if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `\nContainer retention stopped: ${error.message}\n`);
    console.error(error.message);
    process.exitCode = 1;
  }
}
