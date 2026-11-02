# Deployment and manual registry cleanup

## Staging deployment

GitHub Actions runs formatting, builds, and tests, then builds the container image
and pushes its immutable commit tag and moving `staging` tag to DigitalOcean
Container Registry. App Platform's configured Autodeploy handles the rollout when
`staging` is pushed. A small version/database health check confirms the expected
commit is responding before the deployment job finishes.

Registry retention and garbage collection do not run in the deployment job.
Registry cleanup cannot turn a successful application rollout into a failed
`Deploy staging` job. `REGISTRY_RETENTION_MODE` and
`REGISTRY_RETENTION_VERSION_URLS` no longer enable automatic cleanup in CI;
existing values can be removed from GitHub settings if no other workflow uses them.
No DigitalOcean environment-variable change is needed for this workflow change.

## Why old images are not garbage

An old image can still have a commit tag and a manifest referencing its layers.
Age does not make it unreferenced. Deleting only a tag can also leave an untagged
manifest referencing the same layers. Empty garbage collects unreferenced data;
it does not choose which old application versions you want to retire.

A build may appear as three entries: the tagged image index, the runnable image,
and a small provenance attestation. These are related artifacts from one build.
The `staging` and commit tags can both point to the same index. Untagged child
manifests can belong to retained builds; do not bulk-delete them just for lacking tags.

## Manual cleanup in DigitalOcean

1. Check the current deployed commit at the staging app's `/health/version`
   endpoint. Check every other environment using this repository too. Keep their
   image versions, and retain a few known working versions for rollback.
2. In Container Registry, expand the repository and select genuinely obsolete
   versions. Use **Delete image(s)** / **Delete Digest** to remove their manifests
   and associated tags. Removing only a tag may leave its image data referenced.
3. Once unused data is available, click **Empty garbage** to reclaim it. Shared
   layers needed by retained images stay stored, so displayed image sizes do not
   directly predict the space recovered.

DigitalOcean also offers **Delete untagged manifests** during collection. Inspect
untagged entries and retained build references before using it; an untagged entry
is not proof that an artifact is obsolete. Standard garbage collection temporarily
makes the registry read-only, so run it outside deployment/image-push windows.

No registry images or garbage are deleted automatically by this workflow.

## Optional existing retention utility

`.github/scripts/registry-retention.mjs` remains available for separately operated
diagnostics. Its default mode is a read-only report, and its existing deletion
safeguards remain unchanged. It is not invoked by the deployment workflow. There
is no need to configure or run it for the manual cleanup process above.

Its offline tests need Node.js 22 and no provider credentials:

```bash
node --test .github/scripts/registry-retention.test.mjs
```

References:
- [DigitalOcean container-image autodeploy](https://docs.digitalocean.com/products/app-platform/how-to/deploy-from-container-images/)
- [DigitalOcean image deletion and garbage collection](https://docs.digitalocean.com/products/container-registry/how-to/clean-up-container-registry/)
- [Docker image attestation storage](https://docs.docker.com/build/metadata/attestations/attestation-storage/)
