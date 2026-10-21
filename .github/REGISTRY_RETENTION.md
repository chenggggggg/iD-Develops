# Container image retention

The staging workflow pushes both an immutable Git commit tag and the moving
`staging` tag. After the deployed version matches the commit and `/health/db`
passes, it runs `.github/scripts/registry-retention.mjs` under the existing
`staging` concurrency lock. Pull requests run its offline regression tests.

## Initial rollout: report only

The default mode is `report`. It makes GET requests only: no images, tags, layers,
or GitHub settings are changed. Open the successful deployment's GitHub Actions
summary, or download the `registry-retention-report` artifact (Markdown and JSON),
to see each digest, its tags, timestamp, decision, and retention reasons.

Until an environment inventory is configured, the report checks staging only and
states that limitation. Its deletion candidates must not be treated as approved
for deletion from a repository also used by other environments.

## Policy

Only this workflow's configured `DO_REGISTRY_NAME` / `DO_IMAGE_REPOSITORY` is
examined. A manifest is retained if any of these conditions apply:

- Its immutable commit tag matches the current workflow commit or a version
  returned by one of the protected environment endpoints.
- It is one of three distinct available previous successful staging images.
  These are selected from successful `main` push/manual runs of `ci-cd.yml`, in
  GitHub's newest-run-first order. Repeated runs, shared digests, failed runs,
  and images already removed from the registry do not consume rollback slots.
  Fewer than three are retained when fewer are available in the history.
- It has any non-commit tag, including `staging`, `production`, a release name,
  or a manual pin such as `keep-2026-10`. All tags on a digest protect that image.
- It was updated within the last seven days, including the exact boundary.
- It is untagged. Its ownership is unknown, so this policy leaves it alone.

Only old manifests whose tags are **all full 40-character lowercase Git hashes**
can be removed. Each run selects at most twenty, oldest first. Additional
candidates are marked `defer` until a later successful deployment.

## Enable deletion after reviewing reports

Set these **GitHub staging environment variables**, without changing source:

| Variable | Value |
| --- | --- |
| `REGISTRY_RETENTION_VERSION_URLS` | A JSON array of `/health/version` URLs for **every environment consuming this image repository**, including staging. |
| `REGISTRY_RETENTION_MODE` | `apply` (default/unset is `report`). |

For example, if both staging and production consume this registry repository:

```json
["https://staging.id-develops.com/health/version", "https://id-develops.com/health/version"]
```

List one URL per deployed environment, not both public and portal aliases of the
same application. Include older environments even if their images have only
commit tags. Do not add environments that use a different image repository.
Whenever another consumer is introduced, update this inventory before deploying
it. Consumers outside App Platform also need a version endpoint in the inventory.

All URLs must use HTTPS, have no credentials or query string, and return
`{"version":"<full Git commit hash>"}`. Every reported version must have a
matching immutable tag in this repository. Unknown versions, unreachable
endpoints, missing tags, incomplete API pagination, and provider errors stop
cleanup before deletion. Staging must still match the workflow commit.

The existing `DIGITALOCEAN_ACCESS_TOKEN` needs `registry:read` for reports and
`registry:delete` for apply, in addition to its existing push permissions.
`GITHUB_TOKEN` uses the deployment job's `actions: read` permission for history.
Neither token is sent to public version endpoints or written to reports.

Apply is allowed only on a `main` push or manual deployment. It rechecks the
registry inventory and every environment before each deletion. Changes stop the
remaining batch. Completed deletions remain recorded if a later operation fails;
cleanup failure fails the job but does not undo the already healthy deployment.
Change the mode back to `report` to disable further deletion.

All workflows or operators that push, roll back, or deploy from this repository
must coordinate with the same `staging` concurrency group (or pause apply during
external changes). The recheck reduces races but is not a lock on external
DigitalOcean operations. Do not run manual registry changes during apply.

## Garbage collection

Deleting a manifest makes its **unreferenced** layers eligible for garbage
collection. Layers shared by retained images stay stored, so summing image sizes
does not predict the space recovered.

The workflow deliberately does not initiate registry-wide garbage collection or
delete unrelated untagged manifests. In DigitalOcean, either:

- Enable automatic garbage collection in its public preview, if available for
  this registry. It collects unreferenced layers without read-only downtime.
- Run **Empty garbage** during a quiet period. Standard collection temporarily
  makes the registry read-only, so avoid deployment/image-push windows.

No registry cleanup is performed locally while developing or testing this PR.
The first live report runs after this workflow is merged and successfully
deploys. Review that report before setting `apply`.

References: [DigitalOcean cleanup](https://docs.digitalocean.com/products/container-registry/how-to/clean-up-container-registry/),
[registry API](https://docs.digitalocean.com/reference/api/reference/container-registries/),
[GitHub workflow history API](https://docs.github.com/en/rest/actions/workflow-runs#list-workflow-runs-for-a-workflow).

## Local verification

Node.js 22 is sufficient; there are no npm dependencies or registry credentials:

```bash
node --test .github/scripts/registry-retention.test.mjs
```

Tests use in-memory registry, deployment-history, and environment responses. They
cover retention decisions, report-only behavior, targeted deletion, pagination,
credential boundaries, concurrent changes, and partial failures.
