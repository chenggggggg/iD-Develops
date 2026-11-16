# First production release (Marketing)

Staging continues to follow `main` automatically. Production is a **separate App
Platform app and database**, released manually from an existing successful staging
build. The production app uses an image digest, not the moving `staging` tag.
The release workflow neither builds nor pushes images and performs no registry cleanup.

Merging B27 prepares this mechanism; it does not create paid infrastructure, change
DNS, send production email, or activate the live site. Complete the setup and
acceptance below when the owner approves activation.

## 1. Create and configure the production app

Create a separate production database and database user. A separate database/user on
an existing managed cluster can reduce costs if isolation, access, capacity, and backup
requirements are satisfied; do not point production at the staging database.
Enable backups and establish a restore procedure before accepting visitor data.

In DigitalOcean App Platform, create one web service from the existing DOCR repository:

- Start with the immutable commit tag of a successful **main** staging deployment.
  Disable **Autodeploy**. Subsequent releases replace that tag with a pinned digest.
- Use the same Linux/AMD64 repository as staging, port `8080`, and `/health/live`
  for the App Platform HTTP health check. The workflow currently supports one web
  service; separate workers/jobs require extending the release policy first.
- Attach `id-develops.com` and `portal.id-develops.com` to this app. Add the records
  DigitalOcean requests and wait for TLS. Keep both domains attached to this same
  application; the host selects the public or portal module. Decide whether `www`
  redirects to the canonical public hostname.
- Store settings below as **runtime** environment variables. Use DigitalOcean's
  encrypted secrets for connection strings, passwords, and provider keys. Do not
  export populated specs into the repository or upload them as CI artifacts.

Creating the initial app starts its initial deployment. Choose the tested image and
production configuration deliberately; keep DNS cutover separate from that action.
Leave `App__Version` unset: it is baked into the tested image. Do not set a conflicting
`DOTNET_ENVIRONMENT`; if present, it must also be `Production`.

| Setting | Production value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `SiteMode` | `Marketing` |
| `ApplicationUrls__PublicBaseUrl` | `https://id-develops.com` |
| `ApplicationUrls__PortalBaseUrl` | `https://portal.id-develops.com` |
| `ConnectionStrings__ApplicationDbContextConnection` | Secret connection string for the separate production database; use its provider-required TLS settings |
| `Admin__Email`, `Admin__Password` | Actual staff address and a strong production-only bootstrap password |
| `Mail__Provider` | `Resend` |
| `Mail__ResendApiKey` | Secret production sending key restricted to the verified domain |
| `Mail__From` | Verified sender, for example `info@id-develops.com` |
| `Mail__To` | Company recipient, for example `info@id-develops.com` |
| `Mail__ContactEmail`, `Mail__SupportEmail` | `info@id-develops.com` |
| `Storage__Provider` | `R2` when publishing exam media or managing uploads |
| `Storage__Bucket`, `Storage__Endpoint`, `Storage__AccessKey`, `Storage__SecretKey` | Production bucket/credentials; restrict access to that bucket |
| `Storage__Prefix` | `production`; do not reuse the staging prefix |
| `Storage__PublicBaseUrl`, `Storage__CdnBaseUrl` | Production media domain, if used |
| `Stripe__SecretKey`, `Stripe__WebhookSecret` | Production-only secrets; currently required by application startup even in Marketing mode |
| `SchedulingProviders__Zoom__Enabled`, `SchedulingProviders__GoogleCalendar__Enabled` | `false` for this information/level-test release unless the owner's staff workflow needs an intentionally configured integration |
| `Cookiebot__Enabled`, `Cookiebot__Cbid` | `true` and the existing domain-group ID; authorize both production hostnames in Cookiebot |
| `Turnstile__Enabled`, `Turnstile__Required` | Preserve the production defaults (`false`); if enabling later, configure valid production-host keys and verify submission behavior |

The remaining configured sender addresses in `appsettings.Production.json` must also
belong to the verified Resend domain. Preserve the company's inbox MX records when
configuring sending DNS. Follow [the Resend setup](../iD-Develops/ENVIRONMENTS.md#transactional-email-with-resend),
including domain authentication and a separate production API key.

Marketing mode blocks public checkout, registration, and customer/teacher login;
providing Stripe configuration does not enable those features. Do not reuse staging
purchase data or production credentials in staging. Configure a production webhook
destination only for the production account/environment it belongs to.

Use separate production credentials for staff sign-in and enable staff two-factor
authentication. Roles and configured staff users are seeded at startup; approved
products, exam content, and level-test publication are **not** seeded automatically.
Copy only reviewed content through an explicit content-transfer process. Do not clone
the staging database, its learners, purchases, test products, or data-protection keys.
Review the published level-test version, time limit, questions, answers, scoring, and
media using direct staff access before announcing the site.
Transfer approved media into production storage and check its references; copying
question text alone does not copy files out of a staging bucket.

The existing application persists its data-protection key ring in the production
database (`DataProtectionKeys`) with application name `iD-Develops`. Keep that database
and key table across deployments and include them in protected backups. No filesystem
volume or new key per release is needed. Losing the key ring invalidates protected
result links and staff sessions. Production and staging must have separate key rings.

If using Cloudflare, bypass caching for portal/authentication routes, `/health/*`, and
examination/result routes. Never cache private result HTML, even though public result
links can be opened without portal sign-in. Existing result responses set no-store,
noindex/nofollow, and no-referrer. Public result URLs are protected links; a record ID
alone must not authorize access. Keep their signing keys and production URLs stable.

## 2. Configure GitHub

Create the GitHub environment **`production`** and restrict deployment branches to
**`main`**. Where your repository plan supports it, add a required reviewer. Manual
dispatch and the main-branch guard remain in place regardless of reviewer availability.
Restrict who can edit workflows and environment configuration.

Add the following variables to that environment (registry variables may instead be
repository variables already shared with staging):

| Variable | Value |
| --- | --- |
| `DO_PRODUCTION_APP_ID` | New production App Platform UUID |
| `DO_STAGING_APP_ID` | Existing staging App Platform UUID; must differ from production |
| `DO_PRODUCTION_SERVICE_NAME` | Exact production web component name, for example `web` |
| `DO_REGISTRY_NAME` | Existing DOCR registry name |
| `DO_IMAGE_REPOSITORY` | Existing image repository name |
| `PRODUCTION_URL` | `https://id-develops.com` |
| `PRODUCTION_PORTAL_URL` | `https://portal.id-develops.com` |
| `STAGING_URL` | Existing staging public origin |

Add environment secret **`DIGITALOCEAN_ACCESS_TOKEN`** with registry pull and app
read/update permissions appropriate to this release. Production application secrets
stay in DigitalOcean; the workflow reads and preserves their configured values without
printing/uploading the app spec. GitHub's built-in token only needs contents/actions read.

The workflow validates the app ID, service, source repository, attached domains,
Production/Marketing runtime settings, and required mail/database/staff configuration.
It cannot prove that an encrypted connection string points at the correct database,
that email DNS is verified, or that a backup is restorable. Check these during setup.

## 3. Promote an approved build

1. Merge the feature PR and allow **CI and staging deployment** on `main` to finish.
   Test the deployed staging build and obtain owner approval for the production release.
2. Open that successful **main** run and copy its numeric run ID from the URL
   (`.../actions/runs/<run-id>`). A PR run, failed run, skipped test step, or successful
   build without a successful staging rollout is rejected. Both Full and Marketing
   browser suites must have executed successfully.
   A successful rerun of failed jobs is supported: the selector checks the latest
   execution of every required job and retains earlier successful prerequisites.
3. Open **Actions > Release production > Run workflow**, select **main**, and enter
   that ID under `release_run_id`. Approve the environment deployment if configured.
4. The workflow pulls the commit-tagged image, checks its revision/platform, records
   the previous active image, pins the production service to the resolved digest,
   and uses `doctl apps update --update-sources --wait`. No `production` tag is needed.
5. It waits for the selected commit in Marketing mode and database readiness, then
   runs GET-only checks of 26 English/Dutch public pages (including legal pages), local images, staff login,
   and blocked registration. The summary records the commit, digest, and previous
   image. Keep those immutable image tags/digests when doing manual registry cleanup.

Staging may advance while production stays on this digest. Selecting an older known
successful run is supported for rollback; it does not have to match today's staging.
The same workflow is intentionally Marketing-only. Enabling the future Full release
requires B28 and an explicit release-policy change, rather than flipping production
configuration accidentally.

## 4. Acceptance before announcement

Use production URLs after the approved initial activation; automated browser fixtures
must **never** be pointed at production. These manual checks intentionally create a
real level-test attempt and send email, so perform them only as the release operator.

- Confirm the owner-approved information, pricing, contact addresses, and images on
  Home, all four service pages, About, Pricing, and Contact in English/Dutch. Verify
  mobile navigation, spacing, keyboard focus, buttons/links, and actual advertised
  destinations. The brighter homepage replacement awaits the owner-supplied B15 asset.
- Confirm neither desktop nor mobile public navigation exposes Login/Portal, including
  while staff are signed in. Direct Admin/SuperAdmin portal access still works;
  customer/teacher entry and registration remain blocked.
- Submit Contact with a controlled real mailbox. Verify company delivery, user
  acknowledgement, Resend delivery status, and spam/junk. Verify authentication DNS
  if delivery is filtered; a successful provider response does not guarantee inbox placement.
- Start the published level test anonymously; check its actual time limit, saved
  answers, navigation, and both manual submission and timer expiry with blank answers.
  Check completion image/form, enter a controlled address, and verify the emailed
  HTTPS result link opens the correct score and question review without portal login.
- Retain that result link across a subsequent approved deployment. Verify it still
  works; altering its protection token must deny access. Confirm result responses
  remain no-store/noindex and are not cached by the CDN.
- Check `/health/version`, `/health/ready`, `/health/db`, runtime errors, mail queues,
  backup status, and any configured provider callbacks. Confirm production contains
  no E2E fixture products or copied staging customer data.

Record the selected CI run, image digest, owner approval, acceptance outcomes, and
previous compatible image in your release record. A green GET smoke check alone
does not certify mail delivery, exam execution, private-result protection, or backups.

## 5. Rollback

Retain the active production image and at least the previous compatible image and
their commit tags. Use [manual registry cleanup](REGISTRY_RETENTION.md) for other images.

To roll back application code, dispatch **Release production** with the previous known
working successful CI run ID. It goes through the same digest pinning and health checks;
the workflow never automatically rolls back or deletes anything after a failed rollout.
If a run times out or is cancelled, inspect the actual App Platform deployment before
starting another one: cancelling GitHub does not cancel DigitalOcean's deployment.

Database migrations run at startup. An image rollback does **not** revert schema or
visitor data. Verify backward compatibility and take/verify a backup before releases
with migrations. Destructive migration recovery requires a separate, explicitly
approved restore plan; do not blindly restore a database and lose new submissions.

## Verification and references

Offline policy/smoke tests require no cloud credentials and cannot deploy:

```sh
node --test .github/scripts/*.test.mjs
```

Keep the full applicable local E2E suite and the existing both-mode CI checks green
before publishing changes. Local E2E uses the existing watcher and disposable fixtures,
never production data. A deployment of this workflow itself is not exercised locally.

- [DigitalOcean container images and digest pinning](https://docs.digitalocean.com/products/app-platform/how-to/deploy-from-container-images/)
- [DigitalOcean app spec image fields](https://docs.digitalocean.com/products/app-platform/reference/app-spec/)
- [doctl apps update](https://docs.digitalocean.com/reference/doctl/reference/apps/update/)
- [GitHub manual workflow runs](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)
- [GitHub deployment environments](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments)
