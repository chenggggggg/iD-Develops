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
- Does not use real Stripe, Resend mail, or Cloudflare R2 credentials
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

When the public and portal hosts are siblings rather than one being a subdomain of the other,
configure `ApplicationUrls__PortalSessionIndicatorCookieDomain` with their shared parent and
give the environment a unique `ApplicationUrls__PortalSessionIndicatorCookieName`. Staging uses
`.id-develops.com` and `.iDDevelops.Staging.PortalSession`. This cookie contains only protected
display state for the public navigation; the authentication cookie remains portal-host-only.

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
- `ApplicationUrls__PortalSessionIndicatorCookieDomain` (required for sibling public/portal hosts)
- `ApplicationUrls__PortalSessionIndicatorCookieName` (use a unique value per shared parent domain)
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
- `Mail__ResendApiKey` (secret; required when `Mail__Provider=Resend`)
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

## Transactional email with Resend

`Local` and CI use `Mail__Provider=File`, which renders messages under
`App_Data/Mail` without contacting an external provider. Development, staging,
and production default to `Resend`. MailKit/MimeKit and SMTP delivery have been
removed; `Mail__Provider=MailKit` is no longer a supported setting.

### Resend and DNS setup

1. In [Resend Domains](https://resend.com/domains), add `id-develops.com` to preserve
   the application's existing sender addresses. Enable sending and add the exact
   DKIM and SPF/return-path DNS records shown by Resend at your DNS provider.
2. Keep Hostnet's existing root-domain MX records for your normal inboxes. Leave
   Resend receiving disabled. Any MX record Resend requires for its sending
   return-path belongs at the hostname shown in its dashboard, not at the root.
   Do not create a second SPF TXT record at an existing SPF hostname. Preserve
   the existing DMARC record while verifying authentication.
3. Wait until Resend marks the sending domain verified. Once verified, addresses
   such as `info@id-develops.com`, `billing@id-develops.com`, and
   `no-reply@id-develops.com` can send without separate SMTP mailbox credentials.
4. In [Resend API Keys](https://resend.com/api-keys), create distinct keys named
   `id-develops-staging` and `id-develops-production`. Use sending-only permissions
   restricted to the verified domain. Store each key privately in the matching
   environment. Keep open/click tracking disabled for transactional messages.

If you instead verify a sending subdomain, change every configured sender to an
address on that verified subdomain. A verified subdomain alone does not verify
sender addresses on the root domain.

### DigitalOcean App Platform

On the web component, open **Settings > Environment Variables** and configure
runtime variables separately for staging and production:

| Variable | Value |
| --- | --- |
| `Mail__Provider` | `Resend` |
| `Mail__ResendApiKey` | The environment's Resend API key; mark it encrypted/secret |
| `Mail__From` | `info@id-develops.com` |
| `Mail__To` | `info@id-develops.com` (company notification recipient) |
| `Mail__ContactEmail` | `info@id-develops.com` |
| `Mail__BillingEmail` | `billing@id-develops.com` |
| `Mail__NoReplyEmail` | `no-reply@id-develops.com` |
| `Mail__ComplianceEmail` | `compliance@id-develops.com` |

Existing sender values can remain if they belong to the verified domain. Update
any explicit `Mail__Provider=MailKit` override together with this deployment;
otherwise startup rejects the obsolete provider. Remove unused SMTP variables:
`Mail__Host`, `Mail__Port`, `Mail__Username`, `Mail__Password`,
`Mail__SecureSocketOptions`, and `Mail__UseSsl`. No SMTP port or password is needed.
Do not change the running older deployment's provider before deploying the new
code. Prepare domain verification and keys first, then activate the new runtime
configuration with the new image. Test staging before deploying production.

### Delivery, retries, and verification

- All existing Razor templates, attachments, and application mail call sites use
  the same `IMailService` interface. No database migration is required.
- Contact notifications and acknowledgements are sent during the request. Product
  notifications use the existing database queue. Pending notifications retry
  automatically after deployment when their scheduled retry becomes due.
- Each Resend request has an idempotency key reused for transient retries. Product
  notifications derive it from the existing queue ID, preserving it between worker
  runs and restarts. Resend retains keys for **24 hours**; this is not an unlimited
  exactly-once guarantee. The application also tracks successful queue sends.
- Only temporary transport, rate-limit, concurrent-request, and server failures
  are retried immediately. Authentication, validation, and quota failures return
  failure; queued jobs remain pending for a later attempt after correction.
- Runtime logs include Resend's email ID when its API accepts a message. Acceptance
  does not prove inbox placement. Use Resend's email detail/events to investigate
  delivery, bounces, or suppression, and inspect SPF/DKIM/DMARC results on received
  messages. No Resend webhook is required for this sending integration; the legacy
  custom `/bouncewebhook` endpoint is not a Resend-compatible webhook receiver.
- After deployment, submit a staging contact message and signup once. Confirm the
  company and user messages in Resend, then check the receiving inboxes and junk
  folders. Exercise a password-reset email and any attachment-bearing exam email.
  Automated local tests use a fake HTTP transport and file mail, never real sends.

References: [Sending API](https://resend.com/docs/api-reference/emails/send-email),
[domain verification](https://resend.com/docs/dashboard/domains/introduction),
[API keys](https://resend.com/docs/dashboard/api-keys/introduction), and
[idempotency](https://resend.com/docs/dashboard/emails/idempotency-keys).

## Payment auditing and reconciliation

The SuperAdmin audit log at the portal's /admin/audit-log records payment confirmation,
product fulfillment, and notification outcomes separately. Application-side checkout
requests, received checkout responses, browser returns, and verified callbacks are also
recorded and correlated by checkout attempt. These are observations made by this app,
not a copy of Stripe internal logs. A missing return means no return was observed; it
does not establish whether the customer left, the network failed, or Stripe failed. Select Commerce or search by
an exact Stripe Checkout Session, PaymentIntent, event reference, or purchaser email.
Expand an event for product/purchaser details, amount in currency minor units, environment,
and delivery failure guidance. 'View related checkout events' groups that session's history.
Historical event statuses describe the state at their timestamps, not a replacement for
checking the latest successful delivery event and Stripe's own payment record.

- A verified payment observation commits independently before product delivery starts.
  A rollback cannot erase that evidence. Failure to persist the observation fails the
  webhook with HTTP 500 so Stripe can retry before application grants are made.
- Product delivery success and its audit record commit with the order, course/credit
  grants, and queued confirmations. Missing purchaser accounts do not count as delivery.
- Delivery failures remain visible beside the confirmed payment. Correct the underlying
  issue, then resend the corresponding event in Stripe; receipt/session deduplication
  prevents duplicate grants and submissions.
- Pending payments, failed payments, and expired checkouts are separate from delivery
  failures. A late negative event cannot overwrite already recorded payment confirmation.
- Notification acceptance/failure is tracked independently. Provider acceptance is not
  proof of inbox placement, and an email failure does not undo product access.
- Local checkout simulations and Stripe test-mode events are labeled. Zero-total
  checkouts explicitly state that no payment was required.
- Audit metadata includes reconciliation references, not checkout form answers, card
  details, API secrets, or webhook bodies. Access remains SuperAdmin-only.

Use the existing Stripe endpoint at the configured public app's /api/stripe-webhook
(for staging: https://staging.id-develops.com/api/stripe-webhook). Confirm the destination's
signing secret matches Stripe__WebhookSecret and that these existing supported event
subscriptions are enabled: checkout.session.completed,
checkout.session.async_payment_succeeded, checkout.session.async_payment_failed, and
checkout.session.expired. Inspect the destination delivery status when expected records
are missing. Endpoint configuration or a failed/missing delivery cannot be diagnosed from
an empty application audit table alone.

No new environment variables or database migration are required for this audit update.
It does not backfill pre-deployment payments or repair orders previously marked completed
without their expected access. Reconcile those against Stripe and the existing order/access
records before taking a recovery action; do not create a second purchase to repair one.

### Purchase history and unused-purchase refunds

My Profile shows durable, account-owned purchase history. Temporary checkout cleanup
does not remove it. The migration imports earlier completed checkout records and
usable audit evidence only where an explicit account reference exists. Guest purchases
are not assigned to an account merely because its email matches, and incomplete older
benefit snapshots require company review.

Add `refund.created`, `refund.updated`, `refund.failed`, and `charge.refunded` to the
existing `/api/stripe-webhook` destination in Stripe Workbench, retaining the checkout
event subscriptions above. Its signing secret remains `Stripe__WebhookSecret`.
`Stripe__SecretKey` must be able to read and create refunds; update those permissions
if using a restricted API key. No additional DigitalOcean variable is required.
Keep staging on Stripe test mode; configure live credentials and the live destination
separately when production is provisioned.

- Self-service refunds cover a full, entirely unused purchase with reliably tracked
  exam/credit benefits. Started exam records, including cancelled/deleted attempts,
  and previously reserved/used credits require contacting the company. Courses,
  external services, unknown benefits, and incomplete older snapshots remain manual.
- Preparing a refund and starting/consuming its benefits use transaction checks.
  The request is persisted before contacting Stripe; its own exam attempts and credit
  lots are held while the outcome is pending or uncertain. Repeated requests reuse the
  same operation. A confirmed refund removes only that purchase's unused benefits.
- Verified callbacks retrieve the provider's current refund state. The existing
  maintenance worker also checks unfinished requests, and checkout delivery checks
  for prior refunds. Partial external refunds require company review. A definitive
  provider failure releases the hold; a later reversal restores credits actually
  revoked by that refund. Other purchases, independent assignments, and exam records
  remain intact.
- If a create response is lost, keep the hold and reconcile before retrying the same
  idempotency key. After 23 hours without a confirmed provider reference, stop issuing
  create requests and flag company review. Do not clear holds or delete financial
  rows to retry an uncertain payment operation. Use the correlated order/refund
  references in Audit Log and Stripe to establish the outcome.
- `Local` purchases use deterministic simulated refunds and never contact Stripe.
  The confirmation screen identifies this explicitly. Unit tests use a fake provider;
  browser tests must use the existing local app and dedicated test data.

This implements the interim unused-product rule only. It does not introduce a legal
refund window, deductions, or changes to Terms & Conditions.

References: [Stripe refunds](https://docs.stripe.com/refunds),
[refund creation](https://docs.stripe.com/api/refunds/create), and
[idempotent requests](https://docs.stripe.com/api/idempotent_requests).

Reference: [Stripe fulfillment](https://docs.stripe.com/checkout/fulfillment) and
[webhook delivery behavior](https://docs.stripe.com/webhooks).

## Browser Regression Tests

Run Playwright against the existing local application on `id.localhost:5000` and
`portal.id.localhost:5000`. Set `IDDEVELOPS_E2E_PUBLIC_BASE_URL` and
`IDDEVELOPS_E2E_PORTAL_BASE_URL` if these local addresses differ.

The catalog, exam lifecycle, and course attachment persistence tests require `IDDEVELOPS_E2E_DATABASE_CONNECTION` to contain
the PostgreSQL connection string for that same running local application. They
create dedicated products, users, exam attempts, or courses, then remove those records
after each test. These fixtures accept only local application and database hosts.
CI supplies its isolated PostgreSQL connection automatically.

```powershell
$env:IDDEVELOPS_E2E_DATABASE_CONNECTION = '<local application database connection string>'
dotnet test tests/iD-Develops.E2ETests/iD-Develops.E2ETests.csproj --filter "FullyQualifiedName~ProductBrowsingTests|FullyQualifiedName~ExpiredExamTests|FullyQualifiedName~CourseAttachmentPersistenceTests"
```

Course attachment browser tests mock the external storage capability, signing, and
file transfer in Local/CI. Course saves, reloads, and enrolled learner access use
the running application and database. They do not upload files to Cloudflare.

## Event Types

Staff manage event types at Portal Calendar > Event types (`/calendar/appointment-types`).
Admins and SuperAdmins manage teacher assignments; teachers manage types they created
and new teacher-created types are assigned to themselves. Students cannot manage types.

Credit requirements are optional. Free types store no credit or consumption-policy
reference and cost zero. Credit-based types require an active credit, positive cost,
and active policy. Existing bookings retain their saved duration, credit cost, and
policy when a type is edited or deactivated. Availability and Zoom connection checks
still apply to bookings, including free sessions.

The `OptionalEventTypeCredits` migration preserves existing paid types and bookings.
Downgrading is blocked while free types or free meetings exist; review those records
before attempting a downgrade rather than assigning fake credits or deleting history.
No additional environment variables or provider setup are required for this change.

## Unified Session Booking

Calendar has one **Book session** action for students, and **Schedule event**,
**Manage roster**, and **Event type** actions for staff. Booking has no separate
sidebar entry. Students book at `/calendar/book-private-session`; staff use
`/calendar/schedule`, with separate session-booking, one-time-event, and recurring-class
sections. Staff visits to the older booking URL redirect to the scheduling page.
`/calendar/roster` contains working hours and availability only; course-editor scheduling
links open the one-time-event section with the course class selected.

Staff can select themselves or an active student as attendee. Teachers
booking on behalf of a student must be the selected host; Admins can use any eligible
host. A staff member attending their own session chooses another host. The attendee
owns the credit reservation; `BookedByUserId` and the durable request retain the actor.

Event types support generated Zoom meetings, a supplied online link, or in-person
delivery with a location. Existing types migrate to Zoom. Meeting links are withheld
from booking options and released through the existing authorized Join workflow.
Existing Zoom/Google credentials and scopes remain sufficient; no new variables are
required. Local browser tests use in-person fixtures and do not create real meetings.

Availability uses Amsterdam working windows, existing teacher events, the attendee's
bookings/hosted meetings, provider readiness, and connected Google Calendar busy periods.
Slots stay visible but disabled when unavailable. Session durations use UTC boundaries
across daylight-saving changes; repeated local times include CET/CEST labels.

The booking request, private calendar event, and attendee credit reservation are saved
together before provider creation. Request IDs prevent replay from provisioning or
spending twice. Shared person locks coordinate appointment bookings with other scheduling
and cancellation paths. A definitive setup failure cleans up and returns reserved credits.
An uncertain provider result keeps the time and credits held; it never automatically
repeats meeting creation. Joining, attendee synchronization, and attendance/no-show credit
resolution are blocked until setup is confirmed.

Staff see unresolved setup under **Schedule event > Book session > Meeting setup needing attention**.
An Admin/SuperAdmin must check the selected host's Zoom and Google Calendar for the
session name/time before resolving it. Delete any orphaned external meeting first.
**Resolve unconfirmed setup** requires explicit confirmation that no external meeting
remains, retries cleanup for known provider IDs, cancels the held reservation, returns
its credits, and records the administrator in the audit log. Failed cleanup retains
the hold. Confirmed sessions use normal cancellation and their saved credit policy.

Private cancellation also cancels its calendar event, freeing the teacher's slot.
Existing meetings, credit history, and staff restrictions are preserved. The legacy
one-time-event section explicitly chooses a course class or student for custom times
and durations outside event-type booking. Those manual sessions retain their existing
rules; routine bookings with credit requirements should use event types.

Scheduling forms group attendees, timing, and delivery, with infrequent settings under
Advanced options. Failed saves retain submitted values and conversion errors. Each form
binds in its own handler so an unused form cannot block submission. A selected teacher
that becomes unavailable remains visible after failure, with a message to change the
teacher or time before retrying. Calendar labels use CET/CEST for the displayed dates.

The `UnifiedAppointmentBooking` migration adds durable requests, the actor, and type
delivery settings. Downgrade is blocked while setup requests remain unresolved.

## Zoom OAuth Setup

Create one user-managed General OAuth app in the Zoom App Marketplace for iD Develops. The app credentials belong in the server environment; teachers never enter a client ID, client secret, or API key.

Configure these exact portal redirect URLs for the environments where Zoom is enabled:

- Local: offline mode; Zoom is not configured
- Development: `http://portal.id.localhost:5003/oauth/scheduling/zoom/callback`
- Staging: `https://portal-staging.id-develops.com/oauth/scheduling/zoom/callback`
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

- Staging: `https://portal-staging.id-develops.com/oauth/scheduling/google-calendar/callback`
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
- Configure the DigitalOcean App Platform HTTP health check to use `/health/live`. This endpoint confirms that the web process can accept requests and deliberately stays healthy while a dependency is recovering.
- Use `/health/ready` for operational readiness checks. It returns HTTP 503 until PostgreSQL is available and initialization has completed. Stripe failures are reported as a degraded state without taking the whole application out of service.
- `/health/db` and `/health/stripe` expose the most recent background probe state without opening a new connection during the health request.
- Database migrations and configured identity seeding run after HTTP startup. Failed database and Stripe probes retry in the background using the `DependencyRecovery` settings in `appsettings.json`.
- Local development uses a file-based mock mail provider by default and writes rendered emails to `App_Data/Mail`.
- Development, staging, and production should keep real integration credentials in env files only.
