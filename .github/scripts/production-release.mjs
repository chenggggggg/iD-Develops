import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const shaPattern = /^[a-f0-9]{40}$/;
const digestPattern = /^sha256:[a-f0-9]{64}$/;
const appIdPattern = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;

function requireCondition(condition, message) {
  if (!condition) throw new Error(message);
}

export function origin(value) {
  let url;
  try { url = new URL(value); } catch { throw new Error('A valid HTTPS production origin is required.'); }
  requireCondition(url.protocol === 'https:' && !url.username && !url.password && !url.search && !url.hash &&
    url.pathname === '/' && !url.port && !/localhost$/.test(url.hostname), 'Production URLs must be HTTPS origins.');
  return url.origin;
}

export function releaseConfig(env) {
  requireCondition(appIdPattern.test(env.DO_PRODUCTION_APP_ID || '') && appIdPattern.test(env.DO_STAGING_APP_ID || ''),
    'Distinct production and staging App Platform IDs are required.');
  requireCondition(env.DO_PRODUCTION_APP_ID !== env.DO_STAGING_APP_ID, 'Production must not target the staging app.');
  requireCondition(/^[a-z0-9][a-z0-9-]*$/.test(env.DO_REGISTRY_NAME || '') &&
    /^[a-z0-9][a-z0-9._/-]*$/.test(env.DO_IMAGE_REPOSITORY || '') &&
    /^[a-z0-9][a-z0-9-]*$/.test(env.DO_PRODUCTION_SERVICE_NAME || ''), 'Registry, repository, and production service names are required.');
  const publicUrl = origin(env.PRODUCTION_URL);
  const portalUrl = origin(env.PRODUCTION_PORTAL_URL);
  requireCondition(publicUrl !== portalUrl && publicUrl !== env.STAGING_URL?.replace(/\/$/, '') &&
    portalUrl !== env.STAGING_URL?.replace(/\/$/, ''), 'Production origins must be distinct from each other and staging.');
  return {
    appId: env.DO_PRODUCTION_APP_ID, service: env.DO_PRODUCTION_SERVICE_NAME,
    registry: env.DO_REGISTRY_NAME, repository: env.DO_IMAGE_REPOSITORY,
    publicUrl, portalUrl,
    image: `registry.digitalocean.com/${env.DO_REGISTRY_NAME}/${env.DO_IMAGE_REPOSITORY}`,
  };
}

export function selectRelease(run, jobs, repository) {
  requireCondition(run?.repository?.full_name === repository && run.head_repository?.full_name === repository &&
    run.path === '.github/workflows/ci-cd.yml' && run.head_branch === 'main' &&
    ['push', 'workflow_dispatch'].includes(run.event) && run.status === 'completed' &&
    run.conclusion === 'success' && Number.isInteger(run.run_attempt) && run.run_attempt > 0 && shaPattern.test(run.head_sha),
  'Select a completed, successful main-branch CI and staging deployment run from this repository.');
  // Failed-job reruns retain earlier successful prerequisites. Read all attempts,
  // then require the latest execution of each required job, never an old success.
  const pages = Array.isArray(jobs) ? jobs : [jobs];
  requireCondition(pages.length > 0 && pages.every(page => Array.isArray(page?.jobs)), 'CI job history is incomplete.');
  const history = pages.flatMap(page => page.jobs);
  requireCondition(pages.every(page => page.total_count === history.length) && history.every(job =>
    Number.isInteger(job.run_attempt) && job.run_attempt > 0 && job.run_attempt <= run.run_attempt), 'CI job history is incomplete.');
  const latest = new Map();
  for (const job of history) {
    const prior = latest.get(job.name);
    requireCondition(!prior || prior.run_attempt !== job.run_attempt, 'CI job names are ambiguous within an attempt.');
    if (!prior || prior.run_attempt < job.run_attempt) latest.set(job.name, job);
  }
  requireCondition(Object.entries({
      'Build, format, and unit tests': ['Verify formatting', 'Build', 'Run unit and integration tests'],
      'Playwright end-to-end tests': ['Run Playwright tests', 'Run Marketing-mode Playwright tests'],
      'Deploy staging': ['Wait for the deployed commit'],
    }).every(([name, steps]) => {
      const job = latest.get(name);
      return job?.head_sha === run.head_sha && job.conclusion === 'success' && job.status === 'completed' &&
        steps.every(step => job.steps?.some(item => item.name === step && item.conclusion === 'success'));
    }),
  'The selected run must have successful build, browser-test, and staging-deployment jobs.');
  return run.head_sha;
}

export function resolveDigest(images, config, sha) {
  const image = images?.length === 1 ? images[0] : null;
  requireCondition(shaPattern.test(sha) && image?.Os === 'linux' && image.Architecture === 'amd64' &&
    image.Config?.Labels?.['org.opencontainers.image.revision'] === sha,
  'The pulled Linux/AMD64 image must identify the selected CI commit.');
  const reference = image.RepoDigests?.find(value => value.startsWith(`${config.image}@`));
  const digest = reference?.slice(config.image.length + 1);
  requireCondition(digestPattern.test(digest || ''), 'The pulled image has no matching registry digest.');
  return digest;
}

export function pinProduction(apps, config, digest) {
  const app = Array.isArray(apps) && apps.length === 1 ? apps[0] : apps;
  requireCondition(app?.id === config.appId && !app.in_progress_deployment && !app.pending_deployment,
    'The production app must match the configured ID and have no deployment in progress.');
  const spec = structuredClone(app.spec);
  requireCondition(spec?.services?.length === 1 && spec.services[0].name === config.service && spec.services[0].http_port === 8080 &&
    ['workers', 'jobs', 'static_sites', 'functions'].every(key => !spec[key]?.length),
  'This release workflow expects one production web service on port 8080 and no other deployable components.');
  const service = spec.services[0];
  requireCondition(service.image?.registry_type === 'DOCR' && service.image.repository === config.repository &&
    (!service.image.registry || service.image.registry === config.registry) &&
    !service.image.deploy_on_push?.enabled &&
    ['github', 'gitlab', 'git', 'bitbucket'].every(key => !service[key]),
  'Configure the production service to use this registry repository with Autodeploy disabled.');
  const keyName = key => key.replaceAll('__', ':').toLowerCase();
  const settings = [...(spec.envs || []), ...(service.envs || [])];
  requireCondition(settings.every(item => item.type !== 'SECRET' || /^EV\[[^\r\n]+\]$/.test(item.value || '')),
    'The provider must return reusable encrypted secret values, not masked or plaintext replacements.');
  const environment = new Map(settings.map(item => [keyName(item.key), item]));
  requireCondition(!environment.has('app:version'), 'Remove App__Version overrides; the selected image supplies its commit version.');
  requireCondition(!environment.has('dotnet_environment') || environment.get('dotnet_environment').value === 'Production',
    'DOTNET_ENVIRONMENT must not override Production with another environment.');
  for (const [key, expected] of Object.entries({
    ASPNETCORE_ENVIRONMENT: 'Production', SiteMode: 'Marketing',
    ApplicationUrls__PublicBaseUrl: config.publicUrl, ApplicationUrls__PortalBaseUrl: config.portalUrl,
    Mail__Provider: 'Resend',
  })) {
    const setting = environment.get(keyName(key));
    requireCondition(setting?.value === expected && setting.scope !== 'BUILD_TIME', `Configure production runtime ${key} before releasing.`);
  }
  for (const key of ['ConnectionStrings__ApplicationDbContextConnection', 'Mail__ResendApiKey', 'Mail__From', 'Mail__To',
    'Admin__Email', 'Admin__Password', 'Stripe__SecretKey', 'Stripe__WebhookSecret']) {
    const setting = environment.get(keyName(key));
    requireCondition(typeof setting?.value === 'string' && setting.value.trim() && setting.scope !== 'BUILD_TIME',
      `Configure production runtime ${key} before releasing.`);
  }
  for (const url of [config.publicUrl, config.portalUrl]) {
    requireCondition(spec.domains?.some(domain => domain.domain === new URL(url).hostname),
      'Attach both configured production domains to the production app before releasing.');
  }
  requireCondition(digestPattern.test(digest), 'A pinned image digest is required.');
  const activeImage = app.active_deployment?.spec?.services?.find(item => item.name === config.service)?.image;
  const previousDigest = activeImage?.digest;
  const previousTag = activeImage?.tag;
  service.image = { ...service.image, registry: config.registry, digest, deploy_on_push: { enabled: false } };
  delete service.image.tag;
  return {
    spec,
    previous: digestPattern.test(previousDigest || '') ? previousDigest : shaPattern.test(previousTag || '') ? previousTag : 'none recorded',
  };
}

function readJson(path) {
  try { return JSON.parse(readFileSync(path, 'utf8')); } catch { throw new Error('Cannot read release metadata JSON.'); }
}

function output(key, value) {
  requireCondition(process.env.GITHUB_OUTPUT, 'GITHUB_OUTPUT is required.');
  appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const [command, first, second] = process.argv.slice(2);
    const config = releaseConfig(process.env);
    if (command === 'select') output('commit', selectRelease(readJson(first), readJson(second), process.env.GITHUB_REPOSITORY));
    else if (command === 'image') output('digest', resolveDigest(readJson(first), config, process.env.RELEASE_COMMIT));
    else if (command === 'spec') {
      const { spec, previous } = pinProduction(readJson(first), config, process.env.RELEASE_DIGEST);
      writeFileSync(second, JSON.stringify(spec), { mode: 0o600 });
      output('previous', previous);
    } else throw new Error('Unknown production release command.');
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
