import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { origin, releaseConfig, selectRelease, resolveDigest, pinProduction } from './production-release.mjs';
import { verifyMarketing } from './marketing-smoke.mjs';

const commit = 'a'.repeat(40);
const digest = `sha256:${'b'.repeat(64)}`;
const previous = `sha256:${'c'.repeat(64)}`;
const repository = 'owner/site';
const env = {
  DO_PRODUCTION_APP_ID: '11111111-1111-1111-1111-111111111111',
  DO_STAGING_APP_ID: '22222222-2222-2222-2222-222222222222',
  DO_PRODUCTION_SERVICE_NAME: 'web', DO_REGISTRY_NAME: 'example', DO_IMAGE_REPOSITORY: 'site',
  PRODUCTION_URL: 'https://example.test', PRODUCTION_PORTAL_URL: 'https://portal.example.test', STAGING_URL: 'https://staging.example.test',
};
const config = releaseConfig(env);
function run() { return { repository: { full_name: repository }, head_repository: { full_name: repository },
  path: '.github/workflows/ci-cd.yml', head_branch: 'main', event: 'push', status: 'completed', conclusion: 'success', head_sha: commit, run_attempt: 1 }; }
function jobs() {
  return { total_count: 3, jobs: [
    ['Build, format, and unit tests', ['Verify formatting', 'Build', 'Run unit and integration tests']],
    ['Playwright end-to-end tests', ['Run Playwright tests', 'Run Marketing-mode Playwright tests']],
    ['Deploy staging', ['Wait for the deployed commit']],
  ].map(([name, steps]) => ({ name, run_attempt: 1, head_sha: commit, conclusion: 'success', status: 'completed', steps: steps.map(name => ({ name, conclusion: 'success' })) })) };
}
function image() { return [{ Os: 'linux', Architecture: 'amd64', Config: { Labels: { 'org.opencontainers.image.revision': commit } }, RepoDigests: [`${config.image}@${digest}`] }]; }
function app() {
  const values = {
    ASPNETCORE_ENVIRONMENT: 'Production', SiteMode: 'Marketing',
    ApplicationUrls__PublicBaseUrl: config.publicUrl, ApplicationUrls__PortalBaseUrl: config.portalUrl,
    Mail__Provider: 'Resend', ConnectionStrings__ApplicationDbContextConnection: 'encrypted database setting',
    Mail__ResendApiKey: 'encrypted API key', Mail__From: 'info@example.test', Mail__To: 'info@example.test',
    Admin__Email: 'staff@example.test', Admin__Password: 'encrypted password', Stripe__SecretKey: 'encrypted key', Stripe__WebhookSecret: 'encrypted key',
  };
  return { id: config.appId, spec: {
    name: 'production', region: 'ams', databases: [{ name: 'production-db' }],
    domains: [{ domain: 'example.test' }, { domain: 'portal.example.test' }],
    envs: Object.entries(values).map(([key, value]) => {
      const secret = ['ConnectionStrings__ApplicationDbContextConnection', 'Mail__ResendApiKey', 'Admin__Password', 'Stripe__SecretKey', 'Stripe__WebhookSecret'].includes(key);
      return { key, value: secret ? `EV[1:opaque-${key}]` : value, scope: 'RUN_TIME', type: secret ? 'SECRET' : 'GENERAL' };
    }),
    services: [{ name: 'web', http_port: 8080, instance_count: 1, image: {
      registry_type: 'DOCR', registry: config.registry, repository: config.repository, tag: commit, deploy_on_push: { enabled: false },
    } }],
  }, active_deployment: { spec: { services: [{ name: 'web', image: { digest: previous } }] } } };
}

test('accepts a successful tested main deployment, including an older rollback run', () => {
  assert.equal(selectRelease(run(), jobs(), repository), commit);
  assert.equal(selectRelease({ ...run(), event: 'workflow_dispatch' }, jobs(), repository), commit);
});
for (const patch of [{ head_branch: 'feature/change' }, { event: 'pull_request' }, { conclusion: 'failure' },
  { status: 'in_progress' }, { path: '.github/workflows/other.yml' }, { head_sha: 'main' }, { head_repository: { full_name: 'fork/site' } }]) {
  test(`rejects unapproved CI metadata ${JSON.stringify(patch)}`, () => assert.throws(() => selectRelease({ ...run(), ...patch }, jobs(), repository)));
}
test('requires successful executed unit, Full, Marketing, and staging verification steps', () => {
  for (const name of ['Run unit and integration tests', 'Run Playwright tests', 'Run Marketing-mode Playwright tests', 'Wait for the deployed commit']) {
    const data = jobs();
    data.jobs.flatMap(job => job.steps).find(step => step.name === name).conclusion = 'skipped';
    assert.throws(() => selectRelease(run(), data, repository));
  }
  assert.throws(() => selectRelease(run(), { ...jobs(), total_count: 101 }, repository));
});
test('accepts paginated successful failed-job reruns with earlier successful prerequisites', () => {
  const data = jobs();
  const rerun = structuredClone(data.jobs[1]);
  data.jobs[1].conclusion = 'failure';
  rerun.run_attempt = 2;
  data.jobs[2].run_attempt = 2;
  const pages = [{ total_count: 4, jobs: [rerun, data.jobs[0]] }, { total_count: 4, jobs: data.jobs.slice(1) }];
  assert.equal(selectRelease({ ...run(), run_attempt: 2 }, pages, repository), commit);
  // A later failed attempt must not borrow a successful result from an older one.
  rerun.conclusion = 'failure';
  data.jobs[1].conclusion = 'success';
  assert.throws(() => selectRelease({ ...run(), run_attempt: 2 }, pages, repository));
});
test('rejects successful jobs for another commit', () => {
  const data = jobs();
  data.jobs[1].head_sha = 'd'.repeat(40);
  assert.throws(() => selectRelease(run(), data, repository));
});
test('resolves the pulled commit image without rebuilding or trusting the moving staging tag', () => {
  assert.equal(resolveDigest(image(), config, commit), digest);
  for (const edit of [data => data[0].Architecture = 'arm64', data => data[0].Config.Labels['org.opencontainers.image.revision'] = 'd'.repeat(40),
    data => data[0].RepoDigests = [`other.example/site@${digest}`]]) {
    const data = image(); edit(data); assert.throws(() => resolveDigest(data, config, commit));
  }
});
test('pins only the production image, preserving domains, database, resources, and encrypted settings', () => {
  const original = app();
  const before = structuredClone(original);
  const pinned = pinProduction([original], config, digest);
  const expected = structuredClone(original.spec);
  expected.services[0].image.digest = digest;
  delete expected.services[0].image.tag;
  assert.deepEqual(pinned.spec, expected);
  assert.deepEqual(original, before);
  assert.equal(pinned.previous, previous);
  const rollback = pinProduction({ ...app(), spec: pinned.spec }, config, previous);
  assert.equal(rollback.spec.services[0].image.digest, previous);
});
for (const [name, edit] of [
  ['staging app', data => data.id = env.DO_STAGING_APP_ID],
  ['pending deployment', data => data.pending_deployment = { id: 'busy' }],
  ['automatic image deployment', data => data.spec.services[0].image.deploy_on_push.enabled = true],
  ['another registry', data => data.spec.services[0].image.registry = 'other'],
  ['another source', data => data.spec.services[0].github = { repo: repository }],
  ['additional worker', data => data.spec.workers = [{ name: 'other' }]],
  ['wrong container port', data => data.spec.services[0].http_port = 5000],
  ['missing domain', data => data.spec.domains.pop()],
  ['Full production mode', data => data.spec.envs.find(item => item.key === 'SiteMode').value = 'Full'],
  ['service overrides app mode', data => data.spec.services[0].envs = [{ key: 'SiteMode', value: 'Full' }]],
  ['alternate-case mode override', data => data.spec.services[0].envs = [{ key: 'sitemode', value: 'Full' }]],
  ['image-version override', data => data.spec.envs.push({ key: 'App__Version', value: 'unknown' })],
  ['environment override', data => data.spec.envs.push({ key: 'DOTNET_ENVIRONMENT', value: 'Staging' })],
  ['build-only connection', data => data.spec.envs.find(item => item.key === 'ConnectionStrings__ApplicationDbContextConnection').scope = 'BUILD_TIME'],
  ['missing sender key', data => data.spec.envs.find(item => item.key === 'Mail__ResendApiKey').value = ''],
  ['masked provider secrets', data => data.spec.envs.find(item => item.key === 'Admin__Password').value = '********'],
]) test(`refuses ${name} before producing a deployable spec`, () => { const data = app(); edit(data); assert.throws(() => pinProduction(data, config, digest)); });

test('release configuration rejects shared app IDs, insecure URLs, and missing names', () => {
  assert.throws(() => releaseConfig({ ...env, DO_STAGING_APP_ID: env.DO_PRODUCTION_APP_ID }));
  assert.throws(() => releaseConfig({ ...env, DO_PRODUCTION_SERVICE_NAME: '' }));
  assert.throws(() => releaseConfig({ ...env, PRODUCTION_URL: env.STAGING_URL }));
  for (const url of ['http://example.test', 'https://user:password@example.test', 'https://example.test/path', 'https://example.test?query=1', 'https://localhost']) assert.throws(() => origin(url));
});

function smokeProvider(override = () => null) {
  const calls = [];
  return { calls, fetchImpl: async (url, options) => {
    calls.push({ url, options });
    const changed = override(url); if (changed) return changed;
    if (url.endsWith('/health/version')) return Response.json({ version: commit, siteMode: 'Marketing' });
    if (url.endsWith('/health/db')) return Response.json({ status: 'healthy' });
    if (url.endsWith('/register')) return new Response('', { status: 404 });
    if (url.endsWith('/login')) return new Response('<h1>Staff sign in</h1>');
    if (url.includes('/images/')) return new Response('image', { headers: { 'content-type': 'image/webp' } });
    return new Response('<h1>Public page</h1><img src="/images/hero.webp">');
  } };
}
const smokeConfig = { publicUrl: config.publicUrl, portalUrl: config.portalUrl, commit };
test('Marketing smoke checks are read-only, localized, and check images without sending credentials', async () => {
  const provider = smokeProvider();
  assert.deepEqual(await verifyMarketing(smokeConfig, provider.fetchImpl), { pages: 26, images: 1 });
  assert.equal(provider.calls.length, 31);
  assert.ok(provider.calls.every(call => !call.options.method && !call.options.headers && call.options.redirect === 'manual'));
});
for (const [name, override] of [
  ['wrong commit', url => url.endsWith('/health/version') ? Response.json({ version: 'd'.repeat(40), siteMode: 'Marketing' }) : null],
  ['wrong mode', url => url.endsWith('/health/version') ? Response.json({ version: commit, siteMode: 'Full' }) : null],
  ['missing page', url => url.endsWith('/nl-nl/about') ? new Response('', { status: 404 }) : null],
  ['visible account link', url => url.endsWith('/en-us') ? new Response('<h1>Home</h1><a class="public-nav-link public-nav-login" href="/login">Login</a>') : null],
  ['fixture products', url => url.endsWith('/en-us/pricing') ? new Response(`<h1>Pricing</h1>Catalog Credits ${'d'.repeat(32)}`) : null],
  ['broken media', url => url.includes('/images/') ? new Response('', { status: 404 }) : null],
  ['registration enabled', url => url.endsWith('/register') ? new Response('<h1>Register</h1>') : null],
]) test(`smoke check detects ${name}`, async () => { const provider = smokeProvider(override); await assert.rejects(verifyMarketing(smokeConfig, provider.fetchImpl)); });

test('production is manual, serialized, main-only, and promotes existing images with no cleanup or build', () => {
  const workflow = readFileSync(new URL('../workflows/production-release.yml', import.meta.url), 'utf8');
  assert.match(workflow, /workflow_dispatch:/);
  assert.doesNotMatch(workflow, /^  (push|pull_request|workflow_run):/m);
  assert.match(workflow, /if: github.ref == 'refs\/heads\/main'/);
  assert.match(workflow, /group: production\n  cancel-in-progress: false/);
  assert.match(workflow, /name: production/);
  assert.match(workflow, /docker pull --platform linux\/amd64/);
  assert.match(workflow, /--spec .*--update-sources/);
  assert.doesNotMatch(workflow, /docker (build|push)|registry-retention|garbage-collection|create-deployment/);
  assert.match(workflow, /if: always\(\)/);
});
