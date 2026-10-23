# iD-Develops

ASP.NET Core portal for courses, examinations, scheduling, products, and student entitlements.

## Prerequisites

- .NET SDK 10
- PostgreSQL 18
- Docker Desktop (optional, for the Compose environments)
- Node.js only when maintaining frontend tooling

## Local setup

```bash
cd iD-Develops
cp .env.example .env
docker compose up --build
```

The local stack runs with external integrations disabled and stores development mail under
`App_Data`. See [ENVIRONMENTS.md](iD-Develops/ENVIRONMENTS.md) for Development, Staging,
Production, Zoom, Google Calendar, Stripe, mail, and object-storage configuration.

To run without Docker, provide `ConnectionStrings__ApplicationDbContextConnection` through
your local environment and use the `Local` ASP.NET Core environment.

## Verification

```bash
cd iD-Develops
dotnet restore iD-Develops.sln
dotnet format iD-Develops.sln --verify-no-changes --no-restore
dotnet build iD-Develops.sln --configuration Release --no-restore
dotnet test tests/iD-Develops.Tests/iD-Develops.Tests.csproj --configuration Release --no-build
```

Browser tests live in `tests/iD-Develops.E2ETests` and target an already-running isolated
application. GitHub Actions provisions its own PostgreSQL service and application instance.
Contact submission tests require `Mail:Provider=File`, disabled Turnstile, and
`IDDEVELOPS_E2E_FILE_MAIL_DIRECTORY` set to the absolute path of that application's
mail pickup directory (normally `iD-Develops/App_Data/Mail` from the repository root).
The tests inspect generated emails and remove only their own uniquely marked messages.

## Delivery workflow

Work is developed on `feature/*`, `fix/*`, or `chore/*` branches and merged into `main`
through pull requests. Pull requests run formatting, build, unit/integration, and Playwright
checks. A successful merge to `main` builds the staging container and deploys the immutable
commit to DigitalOcean.

After a healthy deployment, the workflow reports obsolete container images while
protecting deployed versions and rollback images. Deletion is disabled by default.
See the [registry retention runbook](.github/REGISTRY_RETENTION.md) for reviewing
reports, configuring protected environments, and enabling cleanup.

## Security

Never commit populated `.env` files, credentials, data-protection keys, generated mail,
database exports, or local IDE configuration. Use `.env.example` only as a key-name template
and store deployed credentials in the relevant GitHub environment or hosting platform.

If you discover a security issue, report it privately to the repository owner instead of
opening a public issue.

## License

No open-source license has been selected. Publication of the source does not grant permission
to copy, modify, or redistribute it.
