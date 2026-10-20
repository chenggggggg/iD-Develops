import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { buildPlan, readConfig, renderReport, runRetention } from './registry-retention.mjs';

const now = new Date('2026-10-20T10:00:00Z');
const sha = number => number.toString(16).padStart(40, '0');
const digest = number => `sha256:${number.toString(16).padStart(64, '0')}`;
const currentSha = sha(10);
const stagingUrl = 'https://staging.example.test/health/version';
const productionUrl = 'https://example.test/health/version';
const manifest = (number, tags = [sha(number)], updatedAt = '2026-09-01T00:00:00Z') => ({
  digest: digest(number), tags, updated_at: updatedAt,
});
const run = number => ({ head_sha: sha(number), head_branch: 'main', event: 'push', conclusion: 'success' });
const environment = {
  DO_REGISTRY_NAME: 'example', DO_IMAGE_REPOSITORY: 'courses/web',
  GITHUB_REPOSITORY: 'example/courses', GITHUB_SHA: currentSha,
  STAGING_URL: 'https://staging.example.test',
  GITHUB_REF: 'refs/heads/main', GITHUB_EVENT_NAME: 'push',
  DIGITALOCEAN_ACCESS_TOKEN: 'test-do-token', GITHUB_TOKEN: 'test-github-token',
};
const options = overrides => ({
  currentSha, deployedVersions: [currentSha], successfulVersions: [sha(9), sha(8), sha(7)], now,
  ...overrides,
});
const config = overrides => readConfig({ ...environment, ...overrides });
const applyConfig = overrides => config({
  REGISTRY_RETENTION_MODE: 'apply',
  REGISTRY_RETENTION_VERSION_URLS: JSON.stringify([stagingUrl, productionUrl]),
  ...overrides,
});
const images = () => [manifest(10, [currentSha, 'staging']), ...[9, 8, 7, 6, 5].map(number => manifest(number))];

function fakeProvider(initialImages = images(), { history = [9, 8, 7, 6, 5].map(run), versions = {} } = {}) {
  const state = { images: structuredClone(initialImages), history, versions: { [stagingUrl]: currentSha, [productionUrl]: sha(8), ...versions }, calls: [], beforeRequest: () => {} };
  state.fetchImpl = async (url, init) => {
    state.calls.push({ url, ...init });
    state.beforeRequest(url, init);
    const parsed = new URL(url);
    const json = data => new Response(JSON.stringify(data), { status: 200 });
    if (parsed.host === 'api.digitalocean.com') {
      assert.equal(init.headers.Authorization, 'Bearer test-do-token');
      assert.equal(parsed.pathname.startsWith('/v2/registries/example/repositories/courses%2Fweb/digests'), true);
      if (init.method === 'DELETE') {
        const target = decodeURIComponent(parsed.pathname.split('/').at(-1));
        state.images = state.images.filter(image => image.digest !== target);
        return new Response(null, { status: 204 });
      }
      return json({ manifests: state.images, meta: { total: state.images.length } });
    }
    if (parsed.host === 'api.github.com') {
      assert.equal(init.headers.Authorization, 'Bearer test-github-token');
      return json({ workflow_runs: state.history });
    }
    assert.equal(init.headers.Authorization, undefined);
    assert.equal(init.redirect, 'error');
    return json({ version: state.versions[url] });
  };
  return state;
}

test('defaults to a non-mutating staging-only report; apply requires an explicit inventory', () => {
  const result = config();
  assert.equal(result.mode, 'report');
  assert.equal(result.inventoryConfigured, false);
  assert.deepEqual(result.versionUrls, [stagingUrl]);
  assert.throws(() => config({ REGISTRY_RETENTION_MODE: 'apply' }), /explicit environment inventory/);
  assert.throws(() => applyConfig({ REGISTRY_RETENTION_VERSION_URLS: '[]' }), /nonempty JSON array/);
});

test('rejects invalid mode, branch, event, inventory, version URL, and commit before calling any API', () => {
  for (const invalid of [
    { REGISTRY_RETENTION_MODE: 'true' },
    { GITHUB_SHA: 'unknown' },
    { REGISTRY_RETENTION_VERSION_URLS: '{}' },
    { REGISTRY_RETENTION_VERSION_URLS: '[1]' },
    { REGISTRY_RETENTION_VERSION_URLS: '["http://example.test/health/version"]' },
    { REGISTRY_RETENTION_VERSION_URLS: '["https://example.test/health/version?token=secret"]' },
  ]) assert.throws(() => config(invalid));
  assert.throws(() => applyConfig({ GITHUB_REF: 'refs/pull/10/merge' }), /main-branch/);
  assert.throws(() => applyConfig({ GITHUB_EVENT_NAME: 'pull_request' }), /main-branch/);
});

test('keeps active staging and production digests, and three distinct available successful rollback versions', () => {
  const plan = buildPlan(images(), options({ deployedVersions: [currentSha, sha(5)] }));
  assert.deepEqual(plan.filter(image => image.action === 'delete').map(image => image.digest), [digest(6)]);
  assert.ok(plan.find(image => image.digest === digest(5)).reasons.includes('currently deployed'));
  for (const number of [9, 8, 7]) {
    assert.ok(plan.find(image => image.digest === digest(number)).reasons.includes('successful rollback version'));
  }
});

test('rollback selection ignores deleted images, repeated runs, and tags sharing one digest', () => {
  const input = [manifest(10), manifest(9, [sha(9), sha(8)]), manifest(7), manifest(6), manifest(5)];
  const plan = buildPlan(input, options({ successfulVersions: [sha(20), sha(10), sha(9), sha(9), sha(8), sha(7), sha(6)] }));
  assert.deepEqual(plan.filter(image => image.action === 'delete').map(image => image.digest), [digest(5)]);
});

test('retains named release tags, unknown tags, and untagged images even when old', () => {
  const input = [manifest(10), manifest(1, [sha(1), 'v1.0']), manifest(2, ['production']), manifest(3, []), manifest(4, null)];
  const plan = buildPlan(input, options());
  assert.equal(plan.every(image => image.action === 'keep'), true);
});

test('grace period includes its boundary and future timestamps', () => {
  const input = [manifest(10), manifest(1, [sha(1)], '2026-10-13T10:00:00Z'),
    manifest(2, [sha(2)], '2026-10-13T09:59:59Z'), manifest(3, [sha(3)], '2026-10-21T00:00:00Z')];
  const plan = buildPlan(input, options());
  assert.deepEqual(plan.filter(image => image.action === 'delete').map(image => image.digest), [digest(2)]);
});

test('limits each batch to the twenty oldest eligible images', () => {
  const input = [manifest(10), ...Array.from({ length: 25 }, (_, index) => manifest(index + 20, [sha(index + 20)], new Date(Date.UTC(2026, 8, index + 1)).toISOString()))];
  const plan = buildPlan(input.reverse(), options());
  assert.deepEqual(plan.filter(image => image.action === 'delete').map(image => image.digest), Array.from({ length: 20 }, (_, index) => digest(index + 20)));
  assert.equal(plan.filter(image => image.action === 'defer').length, 5);
});

test('unknown deployed versions and incomplete, malformed or duplicate inventory prevent a plan', () => {
  assert.throws(() => buildPlan(images(), options({ deployedVersions: [sha(99)] })), /cannot be resolved/);
  for (const invalid of [
    manifest(1, ['invalid|tag']), { ...manifest(1), updated_at: 'bad-date' },
    { ...manifest(1), digest: 'sha256:bad' }, manifest(10), manifest(1, [currentSha]),
  ]) assert.throws(() => buildPlan([...images(), invalid], options()));
});

test('report mode makes only GET requests and lists deletion candidates without changing inventory', async () => {
  const provider = fakeProvider();
  const report = await runRetention(config(), { ...provider, now });
  assert.equal(provider.calls.every(call => call.method === 'GET'), true);
  assert.equal(provider.images.length, 6);
  assert.equal(report.images.filter(image => image.action === 'delete').length, 2);
  assert.match(renderReport(report), /Would delete/);
  assert.match(renderReport(report), /Before enabling deletion/);
});

test('apply deletes only obsolete commit manifests in the selected repository, without registry-wide GC', async () => {
  const provider = fakeProvider();
  const report = await runRetention(applyConfig(), { ...provider, now });
  assert.deepEqual(provider.calls.filter(call => call.method === 'DELETE').map(call => decodeURIComponent(new URL(call.url).pathname.split('/').at(-1))), [digest(6), digest(5)]);
  assert.equal(provider.images.length, 4);
  assert.equal(report.images.filter(image => image.action === 'deleted').length, 2);
  assert.equal(provider.calls.some(call => call.method === 'POST'), false);
});

test('an unreachable protected environment prevents all deletion and records the failure', async () => {
  const provider = fakeProvider();
  const reports = [];
  const fetchImpl = (url, init) => url === productionUrl ? new Response('private provider error', { status: 503 }) : provider.fetchImpl(url, init);
  await assert.rejects(runRetention(applyConfig(), { fetchImpl, now, saveReport: report => reports.push(structuredClone(report)) }), /HTTP 503/);
  assert.equal(provider.calls.some(call => call.method === 'DELETE'), false);
  assert.match(reports.at(-1).error, /HTTP 503/);
  assert.equal(JSON.stringify(reports).includes('private provider error'), false);
});

test('a missing immutable tag for production prevents deletion', async () => {
  const provider = fakeProvider(images(), { versions: { [productionUrl]: sha(99) } });
  await assert.rejects(runRetention(applyConfig(), { ...provider, now }), /cannot be resolved/);
  assert.equal(provider.calls.some(call => call.method === 'DELETE'), false);
});

test('unavailable or malformed successful-deployment history prevents deletion', async () => {
  for (const response of [() => new Response(null, { status: 403 }), () => new Response('{}')]) {
    const provider = fakeProvider();
    const fetchImpl = (url, init) => url.startsWith('https://api.github.com/') ? response() : provider.fetchImpl(url, init);
    await assert.rejects(runRetention(applyConfig(), { fetchImpl, now }), /HTTP 403|history is unavailable/);
    assert.equal(provider.calls.some(call => call.method === 'DELETE'), false);
  }
});

test('staging moving away from the workflow commit prevents deletion', async () => {
  const provider = fakeProvider(images(), { versions: { [stagingUrl]: sha(9) } });
  await assert.rejects(runRetention(applyConfig(), { ...provider, now }), /staging has changed/);
  assert.equal(provider.calls.some(call => call.method === 'DELETE'), false);
});

test('failed runs, PR runs, and non-main runs never consume rollback slots', async () => {
  const history = [{ ...run(6), conclusion: 'failure' }, { ...run(5), event: 'pull_request' },
    { ...run(6), head_branch: 'feature/example' }, ...[9, 8, 7].map(run)];
  const provider = fakeProvider(images(), { history });
  const report = await runRetention(config(), { ...provider, now });
  assert.deepEqual(report.images.filter(image => image.action === 'delete').map(image => image.digest), [digest(6), digest(5)]);
});

test('registry pagination is exhausted before planning; continuation URLs never receive credentials', async () => {
  const provider = fakeProvider();
  let pages = 0;
  const fetchImpl = (url, init) => {
    if (url.startsWith('https://api.digitalocean.com/')) {
      pages += 1;
      const first = new URL(url).searchParams.get('page') === '1';
      return new Response(JSON.stringify({ manifests: first ? images().slice(0, 3) : images().slice(3),
        links: first ? { pages: { next: 'https://untrusted.example.test/next' } } : {}, meta: { total: 6 } }));
    }
    return provider.fetchImpl(url, init);
  };
  const report = await runRetention(config(), { fetchImpl, now });
  assert.equal(pages, 2);
  assert.equal(report.images.length, 6);
});

test('incomplete pagination and provider errors prevent deletion', async () => {
  for (const body of [{ manifests: images(), meta: { total: 7 } }, { error: 'missing inventory' }]) {
    const provider = fakeProvider();
    const fetchImpl = (url, init) => url.startsWith('https://api.digitalocean.com/') ? new Response(JSON.stringify(body)) : provider.fetchImpl(url, init);
    await assert.rejects(runRetention(applyConfig(), { fetchImpl, now }), /incomplete/);
    assert.equal(provider.calls.some(call => call.method === 'DELETE'), false);
  }
});

test('successful history is paginated until three available distinct rollback images are found', async () => {
  const provider = fakeProvider();
  let pages = 0;
  const fetchImpl = (url, init) => {
    if (url.startsWith('https://api.github.com/')) {
      pages += 1;
      return new Response(JSON.stringify({ workflow_runs: pages === 1 ? Array.from({ length: 100 }, (_, index) => run(index + 100)) : [9, 8, 7].map(run) }));
    }
    return provider.fetchImpl(url, init);
  };
  const report = await runRetention(config(), { fetchImpl, now });
  assert.equal(pages, 2);
  assert.equal(report.images.filter(image => image.reasons.includes('successful rollback version')).length, 3);
});

for (const change of ['new image', 'release pin', 'environment version']) {
  test(`${change} appearing after planning aborts before deletion`, async () => {
    const provider = fakeProvider();
    let inventories = 0;
    provider.beforeRequest = (url, init) => {
      if (url.startsWith('https://api.digitalocean.com/') && init.method === 'GET' && ++inventories === 2) {
        if (change === 'new image') provider.images.push(manifest(99));
        if (change === 'release pin') provider.images.find(image => image.digest === digest(6)).tags.push('keep');
        if (change === 'environment version') provider.versions[productionUrl] = sha(6);
      }
    };
    await assert.rejects(runRetention(applyConfig(), { ...provider, now }), /changed after planning/);
    assert.equal(provider.calls.some(call => call.method === 'DELETE'), false);
  });
}

test('partial deletion is reported accurately when a subsequent delete fails', async () => {
  const provider = fakeProvider();
  let lastReport;
  const fetchImpl = (url, init) => init.method === 'DELETE' && url.endsWith(encodeURIComponent(digest(5)))
    ? new Response('private error', { status: 403 }) : provider.fetchImpl(url, init);
  await assert.rejects(runRetention(applyConfig(), { fetchImpl, now, saveReport: report => { lastReport = structuredClone(report); } }), /HTTP 403/);
  assert.equal(lastReport.images.find(image => image.digest === digest(6)).action, 'deleted');
  assert.equal(lastReport.images.find(image => image.digest === digest(5)).action, 'delete');
  assert.match(renderReport(lastReport), /Not deleted/);
});

test('a release pin added between deletions stops the remaining batch', async () => {
  const provider = fakeProvider();
  let inventories = 0;
  let lastReport;
  provider.beforeRequest = (url, init) => {
    if (url.startsWith('https://api.digitalocean.com/') && init.method === 'GET' && ++inventories === 3) {
      provider.images.find(image => image.digest === digest(5)).tags.push('keep');
    }
  };
  await assert.rejects(runRetention(applyConfig(), {
    ...provider, now, saveReport: report => { lastReport = structuredClone(report); },
  }), /changed after planning/);
  assert.equal(provider.calls.filter(call => call.method === 'DELETE').length, 1);
  assert.equal(lastReport.images.find(image => image.digest === digest(6)).action, 'deleted');
  assert.ok(provider.images.some(image => image.digest === digest(5)));
});

test('workflow runs retention only after health verification, inside the deployment lock, in report mode by default', () => {
  const workflow = readFileSync(new URL('../workflows/ci-cd.yml', import.meta.url), 'utf8');
  const deploy = workflow.slice(workflow.indexOf('  deploy-staging:'));
  assert.ok(deploy.indexOf('- name: Report or apply image retention') > deploy.indexOf('- name: Wait for the deployed commit'));
  assert.match(deploy, /group: staging/);
  assert.match(deploy, /REGISTRY_RETENTION_MODE: \$\{\{ vars.REGISTRY_RETENTION_MODE \|\| 'report' \}\}/);
  assert.match(deploy, /actions: read/);
  assert.match(deploy, /if: always\(\)/);
});
