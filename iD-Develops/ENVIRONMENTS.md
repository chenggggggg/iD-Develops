# Environments

This app supports four first-class ASP.NET Core environments:

- `Local`
- `Development`
- `Staging`
- `Production`

## Configuration Rules

- `appsettings*.json` files hold safe, non-secret defaults only.
- Docker env files hold secrets.
- Environment variables override `appsettings` values at runtime.
- `Local` is the safe default for normal coding.
- `Development` is local-with-integrations and should be used intentionally.

## Environment Files

- `appsettings.json`: shared defaults
- `appsettings.Local.json`: local machine defaults with internet integrations disabled
- `appsettings.Development.json`: local machine defaults with internet integrations enabled
- `appsettings.Staging.json`: staging defaults
- `appsettings.Production.json`: production defaults
- `.env`: local Docker-only values for `docker-compose.yml`
- `.env.development`: local integration secrets for `docker-compose.development.yml`
- `.env.staging`: staging secrets for `docker-compose.staging.yml`
- `.env.production`: production secrets for `docker-compose.production.yml`
- `.env.example`: safe placeholder template for creating any of the files above

Copy `.env.example` to the environment-specific filename you need, then replace
the placeholders locally. Never commit the populated file.

## Docker Stacks

### Local

Use the base compose file:

```bash
docker compose up --build
```

Behavior:

- Runs as `Local`
- Uses the Compose project name `iddev-local`
- Uses a local PostgreSQL container
- Disables file uploads intentionally
- Uses file-based local mail
- Does not use real Stripe, SMTP mail, or Cloudflare R2 credentials
- Uses `LOCAL_DB_NAME`, `LOCAL_POSTGRES_USER`, and `LOCAL_POSTGRES_PASSWORD` from `.env`

### Development

Use the development compose file:

```bash
docker compose -f docker-compose.development.yml up --build
```

Behavior:

- Runs as `Development`
- Uses the Compose project name `iddev-development`
- Uses a local PostgreSQL container
- Exposes the web app on `http://localhost:5003`
- Exposes PostgreSQL on `localhost:5433`
- Enables Cloudflare R2 object storage uploads
- Uses local integration secrets from `.env.development`
- Should use test-mode Stripe keys, development R2 prefix/bucket, and test/staging mail credentials
- Defaults `DEVELOPMENT_DB_NAME` to `iddevelops_development` if not set

### Staging

Use the staging compose file:

```bash
docker compose --env-file .env.staging -f docker-compose.staging.yml up --build -d
```

Behavior:

- Runs as `Staging`
- Uses the Compose project name `iddev-staging`
- Uses `.env.staging` for staging secrets
- Must be launched with `--env-file .env.staging` so Compose interpolation and container env both use staging values
- Enables Cloudflare R2 object storage uploads
- Uses a dedicated staging PostgreSQL container
- Defaults `DB_NAME` to `iddevelops_staging` if it is not set in `.env.staging`

### Production

Use the production compose file:

```bash
docker compose --env-file .env.production -f docker-compose.production.yml up --build -d
```

Behavior:

- Runs as `Production`
- Uses the Compose project name `iddev-production`
- Uses `.env.production` for real production secrets
- Keeps secrets out of the repository
- Assumes the production connection string is supplied through `.env.production`

## Application domains

The public website and portal run in the same ASP.NET Core process but use separate hostnames.
Configure both hostnames in every deployed environment:

- `ApplicationUrls__PublicBaseUrl`: the bilingual public website, for example `https://id-develops.com`
- `ApplicationUrls__PortalBaseUrl`: the English-only authenticated portal, for example `https://portal.id-develops.com`

For local development, use `http://id.localhost:5000` for the public website and
`http://portal.id.localhost:5000` for the portal. Both resolve to the existing local application;
do not start a second application instance. In DigitalOcean App Platform, attach both custom
domains to the same web component.
The shared `id.localhost` parent is intentional: browsers reject response cookies scoped to
`.localhost`, while `.id.localhost` lets the protected portal-session indicator reach both hosts.

The codebase treats these hostnames as separate presentation modules. Public Razor Pages
own culture-prefixed routes such as `/en-us/products`; portal Razor Pages under
`Pages/Portal` own cultureless routes such as `/products` and `/courses`. A page endpoint
belongs to only one module. The modules share application services and database access,
but public and portal routes, layouts, authentication behavior, and page models remain
independently addressable.

Portal authentication cookies remain host-only. When a public product requires an account,
the application uses a short-lived protected handoff through the portal and returns to the
public checkout without sharing the portal cookie with the public hostname.

## Important environment keys

- `ConnectionStrings__ApplicationDbContextConnection`
- `ApplicationUrls__PublicBaseUrl`
- `ApplicationUrls__PortalBaseUrl`
- `LOCAL_POSTGRES_USER`
- `LOCAL_POSTGRES_PASSWORD`
- `POSTGRES_USER`
- `POSTGRES_PASSWORD`
- `Storage__Bucket`
- `Storage__Region`
- `Storage__Endpoint`
- `Storage__PublicBaseUrl`
- `Storage__CdnBaseUrl`
- `Storage__AccessKey`
- `Storage__SecretKey`
- `Storage__Prefix`
- `ProductFiles__MaxImageBytes`
- `ProductFiles__MaxDownloadBytes`
- `ProductFiles__ImageCacheControl`
- `ProductFiles__DownloadCacheControl`
- `Stripe__SecretKey`
- `Stripe__WebhookSecret`
- `SchedulingProviders__Zoom__ClientId`
- `SchedulingProviders__Zoom__ClientSecret`
- `SchedulingProviders__Zoom__WebhookSecret`
- `SchedulingProviders__Zoom__Enabled` when overriding the environment default
- `SchedulingProviders__GoogleCalendar__ClientId`
- `SchedulingProviders__GoogleCalendar__ClientSecret`
- `SchedulingProviders__GoogleCalendar__Enabled` when overriding the environment default
- `Mail__Provider`
- `Mail__From`
- `Mail__To`
- `Mail__Host`
- `Mail__Port`
- `Mail__Username`
- `Mail__Password`
- `Mail__SecureSocketOptions`
- `Mail__ContactEmail`
- `Mail__BillingEmail`
- `Mail__NoReplyEmail`
- `Mail__ComplianceEmail`
- `Cookiebot__Enabled`
- `Cookiebot__Cbid`
- `Cookiebot__BlockingMode`
- `Cookiebot__DeclarationEnabled`
- `Admin__Email`
- `Admin__Password`
- `SuperAdmin__Email` and `SuperAdmin__Password` when a separate SuperAdmin account is required

`Stripe__SecretKey` and `Stripe__WebhookSecret` are optional only in the `Local` environment. `Development`, `Staging`, and `Production` require both because payment fulfillment depends on signed Stripe webhooks.

## Zoom OAuth Setup

Create one user-managed General OAuth app in the Zoom App Marketplace for iD Develops. The app credentials belong in the server environment; teachers never enter a client ID, client secret, or API key.

Configure these exact portal redirect URLs for the environments where Zoom is enabled:

- Local: offline mode; Zoom is not configured
- Development: `http://portal.id.localhost:5003/oauth/scheduling/zoom/callback`
- Staging: `https://portal.staging.id-develops.com/oauth/scheduling/zoom/callback`
- Production: `https://portal.id-develops.com/oauth/scheduling/zoom/callback`

Use the Zoom app's Development credentials for both the Development and Staging deployments while the app is being built and tested. Because Zoom requires an HTTPS primary redirect, use the Staging callback as the primary Development-tab redirect. To test OAuth against the laptop Docker deployment, expose port 5003 through an HTTPS tunnel, add that tunnel's exact callback to the Development OAuth allow list, and open the portal through the tunnel URL. Use the separate Production credentials and Production redirect only for the live Production deployment when the app is ready for Marketplace review or publication. Never mix the Zoom app's Development and Production credential pairs.

On the Scopes page, add only the API methods used by the portal: Get a user, Create a meeting, Update a meeting, Delete a meeting, Add a meeting registrant, Delete a meeting registrant, and Get past meeting participants. Zoom maps those methods to the current granular scopes. Set `SchedulingProviders__Zoom__ClientId` and `SchedulingProviders__Zoom__ClientSecret` in the matching `.env.*` file, then recreate the web container.

The connected teacher must be a Licensed Zoom host; registrant deletion and past-participant attendance reporting require a Pro plan or higher. Students do not connect a Zoom account. A confirmed portal booking is registered server-side and receives a personal, encrypted Zoom join URL that is released only through the portal's time-limited Join action.

Enable Event Subscription and configure this notification endpoint:

- Development through a temporary HTTPS tunnel: `https://<tunnel-domain>/api/zoom/webhook`
- Staging: `https://staging.id-develops.com/api/zoom/webhook`
- Production: `https://id-develops.com/api/zoom/webhook`

Subscribe to `meeting.participant_joined`, `meeting.participant_left`, and `meeting.ended`. Store the app's webhook Secret Token as `SchedulingProviders__Zoom__WebhookSecret`. The endpoint validates Zoom's URL challenge and verifies every notification using `x-zm-signature`; the deprecated verification token is not used.

The attendance defaults are 15 minutes and 25 percent of scheduled duration, with both thresholds required. Override them with `SchedulingProviders__Zoom__AttendanceMinimumMinutes` and `SchedulingProviders__Zoom__AttendanceMinimumPercent` if the business policy changes.

The `data_protection_*_keys` Docker volumes contain the encryption key ring used for OAuth tokens. Preserve these volumes during ordinary deployments; removing one makes existing provider connections require reconnection.

## Google Calendar OAuth Setup

Create a Google Cloud project for non-production testing and enable the Google Calendar API. Configure the Google Auth Platform consent screen for an External audience, then create an OAuth client with application type **Web application**.

Request these exact scopes in the consent configuration:

- `openid`
- `email`
- `https://www.googleapis.com/auth/calendar.events.owned`
- `https://www.googleapis.com/auth/calendar.events.freebusy`

Configure these exact authorized redirect URIs:

- Staging: `https://portal.staging.id-develops.com/oauth/scheduling/google-calendar/callback`
- Production: `https://portal.id-develops.com/oauth/scheduling/google-calendar/callback`

Local and Development keep Google Calendar disabled. Add test Google accounts while the consent screen is in Testing status. Store the Web client ID and client secret as `SchedulingProviders__GoogleCalendar__ClientId` and `SchedulingProviders__GoogleCalendar__ClientSecret` in the deployment environment, and set `SchedulingProviders__GoogleCalendar__Enabled=true` for Staging or Production.

Connecting an account automatically synchronizes all future scheduled events to the teacher's primary calendar. The Settings page also provides a manual **Sync** action. Disconnecting removes future portal-created calendar events before revoking the Google token.

## Storage Prefixes

Use separate prefixes at minimum:

- `development`
- `staging`
- `production`

Separate buckets for production and non-production are safer than sharing one bucket.

For Cloudflare R2, use:

- `Storage__Provider=R2`
- `Storage__Region=auto`
- `Storage__Endpoint=https://<R2_ACCOUNT_ID>.r2.cloudflarestorage.com`
- `Storage__PublicBaseUrl=<R2_PUBLIC_OR_CUSTOM_DOMAIN>`
- `Storage__CdnBaseUrl=<R2_PUBLIC_OR_CUSTOM_DOMAIN>`

## Notes

- PostgreSQL Docker health checks use `pg_isready`.
- Local development uses a file-based mock mail provider by default and writes rendered emails to `App_Data/Mail`.
- Development, staging, and production should keep real integration credentials in env files only.
