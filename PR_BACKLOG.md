# Planned PR backlog

Updated: 2026-11-26

This is the combined planning backlog from the staging review and owner meeting.
B01-B44 are planning IDs, not GitHub PR numbers. Current launch priorities are:
P0 marketing-release blockers, P1 supporting work, P2 post-launch improvements.
This file does not authorize implementation, Git changes, or deployments.

Workflow: keep only one active PR. Before each PR, summarize its ID/title, priority,
estimated size, and planned changes for the user. Verify and commit the complete intended
change set, publish it, and confirm that PR is merged before beginning the next PR or branch.
Preserve uncommitted work and confirm the working tree is clean before switching tasks.
Follow AGENTS.md for Git-operation approvals. B01 was explicitly started on 2026-10-31
on fix/payment-fulfillment-audit.

## High-priority implementation queue - 2026-11-25

The user explicitly prioritized and authorized implementation, branches, publication,
and merging for B40-B42, then added B43 to the same work. Process these as separate
PRs in order, waiting for successful GitHub checks before every merge. B40 is merged
as PR #77; B41 is merged as PR #78 after all GitHub checks passed. B42 is merged as PR #79; B43 is merged as PR #80. B44 is in progress with separate implementation, publishing, and merge approval. Production release remains
a separate workflow.

## Marketing launch priority update - 2026-11-24

B37 is the next top-priority P0 implementation after the current PR is merged. It takes
precedence over B35 and other supporting/post-launch work: provide one general level-test
page offering A1 and A2, with each selected test retaining its own introduction and results.
This saves the requested scope only; it does not authorize implementation or Git operations.

## Production review priorities - 2026-11-21

User requested these production issues at P0. This is planning only: no production
configuration, account roles, authentication policy, or application code changed.

Observed during this review:

- PR #67 is merged as 9b0ea8c0d0fed392d78c0dee29e7e9c0f9a26389. It includes the native
  horizontal-scroll/mobile examination fix and its long-exam regressions.
- Production /health/version still reports e49c7f9c3ea6f5636f8cd2ddab0d33b2a928b6ed,
  SiteMode=Marketing. Staging reports 931529d1453258cecdee908173052d1227717dd3,
  SiteMode=Full. These are a point-in-time deployment snapshot, not a permanent status.
- Publish the already-tested PR #67 build through the production promotion workflow
  after its required CI/staging checks. Do not open a duplicate native-scroll fix PR.
- Desktop click-and-drag scrolling is not implemented by native overflow alone; B34
  tracks pointer/touch interaction verification and any remaining implementation.
- Existing Signup products use product details -> personal details -> product questions
  -> confirmation. Full name/email are required; phone/address fields are optional.
  This is an existing Signup-type workflow, not a new mandatory step for every product type.
- Marketing currently blocks public product actions independently of Preview only.
  B31 is the newly requested policy change: allow eligible public signup/Koalendar
  actions while retaining closed customer accounts/Portal access.
- Production has Admin__Email/Admin__Password settings and no SuperAdmin__Email or
  SuperAdmin__Password settings. The seeder assigns explicit configured roles; it
  does not promote an arbitrary first-created user. B33 covers owner configuration.
- An anonymous HEAD to https://portal.id-develops.com/ returns 403; /login returns 200.
  SiteAccessMiddleware blocks the anonymous Portal request before the normal Identity
  login challenge. B32 restores that challenge and preserves the intended destination.

Immediate release action: promote/verify #67. Code follow-up order: B31 -> B32 -> B34;
B33 now also standardizes owner-only startup and release checks; hosted owner configuration remains a separate P0 step.
All previous pending PRs, including the B09 contact Reply-To follow-up, remain saved.
## Marketing launch scope - owner priorities, 2026-11-10

The first production release presents the company and its services, provides contact,
and supports the full public level-test journey through emailed results. The existing
portal remains available through its direct URL for Admin/SuperAdmin operations.
The original B23 SiteMode=Marketing policy disables public registration, customer/teacher
login and existing sessions, public signup/checkout, and customer Portal access on the
server. Public information, contact, anonymous level testing, and protected results stay
available. SiteMode=Full restores normal customer features; existing authorization still
applies. B31 updates the product restriction to permit eligible account-free signup/enquiry
and external booking; Stripe checkout and account-dependent product actions remain disabled
in Marketing. These application modes are separate from Development/Staging/Production.

Launch order: B10 -> B22 -> B23 -> B24 -> B25 -> B26 -> B27.
B15 asset work can run alongside these; broken visible assets are launch blockers,
while exhaustive unused-image cleanup is supporting work. B10 already covers the
completion wallpaper. Its current layout shows the image on desktop and prioritizes
the form on mobile. The brighter homepage image is merged in PR #61; the remaining B15
asset audit and cleanup are merged in PR #64.

Public result viewing remains independent of portal login. Retain the protected emailed
link; a record ID alone must not grant access. Signed-in attempts keep their account
association, while Marketing attempts remain anonymous. B28 must implement verified account
association and account-protected results for the Full launch. Keep historical Marketing
links working unless an explicit access-transition policy is approved. Link expiry/reissue
and a shorter result URL remain separate decisions.

The wider B12/B20 audits, B13 editor polish, B14 product-editor improvements, B16-B18 blog
work, and B03 Zoom architecture remain in the post-launch backlog. B24 brings forward
only the public design foundations needed for the marketing pages.

## Update size estimates

These are scope/complexity estimates including implementation and required verification,
not calendar deadlines. External setup, pending decisions, and supplied assets can delay
work without changing its size. Estimates may change after a focused investigation.

- **Small:** localized change to existing behavior or presentation, with focused checks.
- **Medium:** a contained workflow or component spanning several files/layers, potentially
  a small migration, with unit/integration and relevant browser verification.
- **Large:** several related workflows, roles, data changes, or integrations; substantial
  regression coverage and usually multiple implementation stages.
- **Extra large:** a subsystem-sized feature. Break it into smaller reviewable PRs before
  implementation rather than treating the estimate as permission for one enormous PR.
- **Audit effort:** review/report scope only; proposed fixes receive their own estimates.

| Planning ID | Size | Main reason |
| --- | --- | --- |
| B01 | Large | Payment/webhook/audit persistence across transaction boundaries, reconciliation details, and failure/retry integration tests. |
| B02 | Large | The message-only correction is Small; the proposed pending-setup lifecycle needs scheduling, provider provisioning, visibility, and retry changes. |
| B03 | Large | New account authorization and teacher host mapping, existing meeting lifecycle compatibility, and integration tests; depends on Zoom account/licensing decisions. |
| B04 | Medium | Extend existing appointment models, nullable credit rules/migration, management validation, role permissions, and tests. |
| B05 | Large | Student and staff booking flows, booking-on-behalf permissions, atomic credit handling, availability/concurrency, and cancellation/provider regressions. |
| B06 | Large | Product configuration and checkout requirements, versioned exam entitlements, payment fulfillment retries, assignment visibility, and end-to-end coverage. |
| B07 | Medium | Date/timezone queries, filtering, bounded result loading, table details, and browser regressions on an existing page. |
| B08 | Medium | Shared progress components/scripts, portal navigation behavior, public navigation cleanup, and duplicate-submit/retry browser checks. |
| B09 | Large | Separate product/contact/question steps, required contact identity, optional phone/address details, atomic notification queuing, and protected public/editor/preview regressions. |
| B10 | Medium | Results layout/data presentation plus completion imagery and responsive checks; reuse the existing immutable exam data. |
| B11 | Large | Restructure several calendar management workflows across roles and screen sizes while preserving complex scheduling behavior. |
| B12 | Large | Application-wide theme/semantic discovery and shared-component standardization; deliver implementation as smaller coherent PRs rather than one broad rewrite. |
| B13 | Small | Focused existing exam control/pagination corrections and regression checks; no exam lifecycle redesign. |
| B14 | Medium | Persisted/localized CTA labels, editor interactions, preview/public icon behavior, advanced settings placement, and tests. |
| B15 | Medium | The hero replacement alone is Small; the full raster inventory, conversions, responsive assets, cache/reference checks, and image quality verification are broader. |
| B16 | Large | New content models/migrations, public routes, admin management, publication permissions, media, and browser coverage. |
| B17 | Extra large | A structured visual editor, multiple block types, shared rendering, accessible ordering, content validation, and hierarchical TOCs. Split into editor foundation, blocks, and contents/preview sub-PRs before implementation. |
| B18 | Medium | Editorial layout configuration, post ordering/featured selections, responsive previews, and a homepage feed on the B16/B17 foundation. |
| B19 | Medium | Investigation and measurement precede a focused query correction; estimate is provisional until the offending query is identified. |
| B20 | Large (audit effort) | Broad source and safe browser coverage across environments, roles, layouts, and states. This estimates the review/report, not implementation of unknown findings. |
| B21 | Large | Account payment history, purchase-specific usage checks, monetary refunds, payment/provider reconciliation, entitlement reversal, concurrency, and browser regressions. |
| B22 | Medium | Exam-taking alignment, media/prompt layout, readable widths, responsive header offset, and mouse/keyboard/browser regressions. |
| B23 | Large | Reversible Marketing/Full policy, public authentication and transaction gates, existing-session rejection, staff access, and both-mode verification. |
| B24 | Medium | Establish and apply existing public typography, buttons, spacing, forms, and responsive component conventions for the launch pages. |
| B25 | Large overall | Redesign four service pages using approved content and shared patterns; deliver as independently reviewable page batches. |
| B26 | Medium | About-page layout, company information, existing imagery, contact paths, and localized responsive checks. |
| B27 | Medium | Production configuration and data separation, stable result-link keys, production URLs/email, and release smoke checks. |
| B28 | Medium-Large | Verified account registration/sign-in and secure result claiming for Full launch, with historical Marketing links preserved. |
| B31 | Medium | Separate product action capabilities from customer account/Portal access; coordinate public views, server guards, signup/booking actions, preview-only flags, and both-mode tests. |
| B32 | Small | Restore anonymous Portal login challenges, safe return URLs, authenticated-forbidden behavior, and browser/API regressions. |
| B33 | Small-Medium | Standardize one configured SuperAdmin owner across environments; remove automatic Admin seeding, update CI/release configuration, and verify account/password preservation and staff-role management. |
| B34 | Small-Medium | Fix actual touch, wheel, mouse-drag, and keyboard pagination; move mobile question images above related context while retaining desktop columns, answer state, and Submit. |
| B35 | Small-Medium | Add missing product-create and product-navigation progress feedback, prevent repeated creation clicks, and cover delayed responses and retries. |
| B37 | Medium-Large | Shared A1/A2 selection-page design plus public-test configuration/routing, exam-specific introductions and results, historical-link compatibility, and both-mode browser regressions. |

## Completed work excluded from this backlog

GitHub PRs #23-#38 are merged. Completed work includes exam saving/submission,
expired-exam completion and removal of Overdue, product routing, course attachments,
exam version preservation and timing, persistent media-save warnings, contact receipts,
initial loading feedback and its flaky-test fix, product discounts and slugs, checkout
protection, signup reliability, registry retention, and Resend migration.
Resend deployment was discussed separately; merge status does not establish live delivery.

## Source findings before B01 implementation

- `Models/AppointmentType.cs`, `AppointmentTypeTeacher.cs`, and `Services/AppointmentService.cs`
  already provide private-session types, assigned teachers, duration, credit rules,
  availability lookup, and a booking service. Extend these rather than duplicating them
  with a parallel EventType model. Credit type and cost are currently mandatory.
- `Pages/Portal/Schedule/Appointments.cshtml.cs` is staff-only management. Student-facing
  and staff-on-behalf booking need to be wired into a clear unified workflow.
- `Services/SchedulingIntegrationService.cs` obtains the assigned teacher's Zoom token and
  creates the meeting at `/v2/users/me/meetings`. Missing connection validation blocks
  generated Zoom meetings before saving the current workflow.
- `Services/CatalogCheckoutFulfillmentService.cs` writes `PurchaseCompleted` after grants
  and notifications are processed, within the fulfillment transaction. A delivery failure
  before that point does not produce an independent durable payment-received audit event.
  `StripeWebhookService.cs` catches fulfillment exceptions and writes runtime logs.
  Purchase success/failure event types already exist; audit their reliability and display.
- `Pages/Portal/Admin/AuditLog.cshtml.cs` currently restricts access to SuperAdmin, pages
  50 results, and has category/outcome/search filters but no date filters. Preserve that
  access boundary unless the owner explicitly approves broader Admin access.
- `Models/CatalogProduct.cs` already supports course and credit grants, with derived
  account requirements. Paid-exam grants should extend that fulfillment architecture.

## B01 - P0 - Durable payment and fulfillment audit trail

Status: completed and merged in [PR #39](https://github.com/chenggggggg/iD-Develops/pull/39).
App-side request/response/return/callback auditing is included.

**Estimated size: Large.** Payment/webhook/audit persistence across transaction boundaries, reconciliation details, and failure/retry integration tests.

New functionality/reliability PR. Highest priority because money received and delivery
failed must be distinguishable in the administrator's history.

- Record the app-side checkout request, received checkout response, browser return,
  and verified callback. Absence of a return is an unknown outcome, not proof of a Stripe
  failure; Stripe internal diagnostics are outside this PR.
- Correct application-side purchase event creation, transaction rollback, callback
  handling, deduplication, and audit-table filters. No Stripe-internal log collection is required.
- Persist verified payment receipt separately from product delivery outcome, so delivery
  rollback does not erase evidence that payment succeeded.
- Correlate Stripe event/session/payment references, product, purchaser/account, amount,
  currency, and timestamps; distinguish payment pending/succeeded/failed from fulfillment
  pending/succeeded/failed, and notification failure from entitlement failure.
- Preserve webhook signature verification and retry safety. Duplicate or out-of-order
  notifications must not duplicate receipts, grants, assignments, or audit events.
- Show actionable failure details to authorized staff. Provide Stripe references for
  reconciliation without treating email delivery as proof of payment.
- Verify successful purchases, payment success with forced fulfillment failure, webhook
  retries, duplicate callbacks, and the resulting audit-log display.

Dependencies: existing checkout/audit foundation. Separate from B07 table UX.

## B02 - P1 - Recurring-class Zoom readiness and clear errors

**Estimated size: Large.** The message-only correction is Small; the proposed pending-setup lifecycle needs scheduling, provider provisioning, visibility, and retry changes.

Status: completed and merged in [PR #42](https://github.com/chenggggggg/iD-Develops/pull/42).
Verification: Release build, formatting, 398 unit/integration tests, and 34 relevant
Playwright tests passed. Local Zoom is disabled; provider recovery uses simulated
provider tests rather than creating real Zoom meetings.

- Name the selected teacher in connection errors, for example:
  '<teacher name> has not connected Zoom in Portal Settings. Therefore the recurring
  class could not be created.' Distinguish missing, expired, and failed connections.
- Show integration readiness when a teacher is selected, before submission.
- Approved behavior: permit authorized staff to save the schedule as pending Zoom
  setup, then generate the meeting after a valid connection is available.
- Clearly distinguish 'schedule saved; Zoom setup required' from 'meeting ready'.
  Pending setup must not be advertised to students as a ready online booking or send
  misleading join links. Retry provisioning without duplicating meetings/occurrences.
- Continue allowing applicable non-Zoom/manual-link delivery paths with clear validation.

Approved on 2026-11-01: save as staff-only pending; provision after connection. Retry
pending setup automatically, normally within a minute, with an explicit staff retry
action. Publish only occurrences with complete meeting details. Preserve existing
bookings/history and keep individual teacher hosting; company-managed Zoom is B03.

## B03 - P2 - Company-managed Zoom integration and teacher host mapping

**Estimated size: Large.** New account authorization and teacher host mapping, existing meeting lifecycle compatibility, and integration tests; depends on Zoom account/licensing decisions.

Separate architecture PR, pending the Zoom organization/licensing decision.

- Preferred direction when teachers belong to one company Zoom organization: one
  centrally authorized company integration, with each teacher mapped to their own Zoom
  user/host. Investigate server-to-server OAuth with the required account scopes.
- Admins schedule on behalf of mapped hosts without requiring every teacher to complete
  individual portal OAuth setup. Zoom users and suitable licenses still need to exist.
- Preserve a per-teacher authorization path if teachers use external Zoom accounts.
- Explicitly model host identity for creating, updating, canceling, and reconciling
  meetings. Changing the teacher must not make existing meetings unmanageable.
- If a shared host is supported, make it explicit and validate concurrency and start/host
  permissions. Do not silently create every teacher's meetings under the admin's account.
- Alternative hosts must meet Zoom's account/licensing rules. A teacher joining through
  an ordinary participant link is not automatically the host.

Depends on confirmation of the Zoom plan, managed vs external users, and concurrent
class requirements. Can follow B02; does not need to block its messaging fix.

## B04 - P1 - Unified event types with optional credit requirements

Status: completed and merged in [PR #48](https://github.com/chenggggggg/iD-Develops/pull/48).
Release build, full formatting
check, all 467 unit/integration tests, all 168 Playwright tests, and EF model/migration
consistency passed on 2026-11-04. Desktop and mobile screens were inspected.

Free types need no credit catalog or consumption policy; paid types validate both.
Admin-controlled teacher assignments and existing booking snapshots are preserved.
The editor retains failed submissions, and the booking selector waits for its script
before accepting selections. Includes the `OptionalEventTypeCredits` migration and
desktop/mobile inspection; no new environment variables are needed. B05 remains the
separate unified booking and staff-on-behalf workflow.

**Estimated size: Medium.** Extend existing appointment models, nullable credit rules/migration, management validation, role permissions, and tests.

Extend the existing appointment-type foundation.

- Adopt one consistent user-facing term, 'Event types', without duplicating existing data.
- Name, duration, active state, eligible teachers, and optional required credit type/cost.
- Support free event types without fake credits or a mandatory consumption policy.
- Admin/SuperAdmin manage the catalog and teacher assignments; teachers manage permitted
  types for themselves under the existing ownership rules. Students cannot manage types.
- Keep advanced credit/cancellation settings compact and role-restricted.
- Migrate existing appointment types and preserve current bookings and credit history.

Dependencies: no new duplicate model; precedes B05.

## B05 - P1 - Unified student, teacher, and admin appointment booking

Status: completed and merged in [PR #49](https://github.com/chenggggggg/iD-Develops/pull/49).
Includes actor/attendee separation,
durable requests, atomic credit reservation, provider failure/review handling, session
cancellation, and live browser regressions. Build, full formatting verification,
485 unit/integration tests, and all 188 Playwright tests pass. The EF model matches its
migration; desktop/mobile booking views were inspected. Real Zoom/Google provisioning
was not exercised locally; provider outcomes are covered by service tests and browser
fixtures use in-person delivery. Staff scheduling now groups session bookings, one-time
events, and recurring classes; Manage roster contains availability only. The legacy
custom-session form remains under Advanced options in one-time scheduling until its
arbitrary time/duration capabilities are covered; routine booking uses event types.
The separate booking sidebar item is removed. B11 remains the broader calendar/roster redesign.

**Estimated size: Large.** Student and staff booking flows, booking-on-behalf permissions, atomic credit handling, availability/concurrency, and cancellation/provider regressions.

Replace the teacher-only manual 1-on-1 creation UX with a shared booking workflow.

- Calendar entry points: students see **Book session**; staff see **Schedule event**,
  **Manage roster**, and **Event type**. Remove the separate booking sidebar item.
- Staff scheduling separates session booking, one-time events, and recurring classes.
  Manage roster contains availability/working hours only and preserves the selected
  teacher after saving. Course-editor scheduling links target the new scheduling page.
- Student chooses an active event type, eligible teacher, and available time.
- Show duration and required credit/cost or 'No credits required' before confirmation.
- Staff may book for a selected student or for themselves. Distinguish the acting user
  from the attendee/credit owner; never debit an administrator merely for booking on behalf.
- Validate sufficient usable credits on the server and reserve/consume atomically under
  the existing credit policy. Prevent simultaneous requests spending the same credits.
- Respect teacher availability, existing appointments, provider readiness, overlapping
  meetings, daylight-saving transitions, and capacity.
- Keep management inputs staff-only. Preserve cancellations/rescheduling and apply the
  established release/refund policy without duplicate credits or Zoom meetings.
- Maintain existing appointment data and retire duplicate creation entry points only
  when the replacement covers their supported behavior.

Dependencies: B04; coordinate Zoom readiness with B02/B03.

## B06 - P1 - Paid exams attached to products

Status: completed and merged in [PR #44](https://github.com/chenggggggg/iD-Develops/pull/44).
The purchase/version decisions and completed verification are retained below under
Decisions and dependencies.

**Estimated size: Large.** Product configuration and checkout requirements, versioned exam entitlements, payment fulfillment retries, assignment visibility, and end-to-end coverage.

New product/entitlement PR using the existing checkout and exam assignment services.

- Add an optional exam selector to standard products, alongside the existing course
  selector in product edit/settings; a product may sell an exam alone.
- When an exam is selected, show an editable "Attempts on purchase" setting. Default
  it to that exam's configured attempt allowance; the staff member editing the product
  can override it. Snapshot the purchased allowance along with the exam version.
- Require an authenticated account before paying for an exam entitlement, and explain
  this requirement in the public signup/checkout flow.
- Verified successful payment assigns the purchased exam to the purchasing account.
  Browser return URLs alone must never grant access.
- Snapshot the promised exam grant/version consistently with existing exam versions so
  editing a product or exam after checkout cannot silently change what was purchased.
- Make fulfillment idempotent across duplicate webhooks, retries, and success-page visits.
- Show the delivered exam in My Exams; preserve appropriate access and audit entries.
- Record paid-but-unassigned failures through B01. Preserve purchase-to-attempt links
  so later usage checks can identify which purchase supplied an attempt.
- Monetary refunds and payment history belong to B21, not this entitlement PR.
  Approved on 2026-11-02: quantity is fixed at one exam purchase per checkout;
  repeat purchases add their configured attempt allowance, preserving each purchased version.

Dependencies: B01 and existing exam version/assignment architecture.

## B07 - P1 - Audit-log date filters, totals, and load more

**Estimated size: Medium.** Date/timezone queries, filtering, bounded result loading, table details, and browser regressions on an existing page.

Status: completed and merged in [PR #46](https://github.com/chenggggggg/iD-Develops/pull/46).
Verification: Release build, formatting, all 422 unit/integration tests, and all 156
Playwright tests passed on 2026-11-03. Desktop and mobile screenshots were inspected.
No database migration is required.

Expand the previously saved audit-log UX PR.

- Default to today's logs using the application's Amsterdam business timezone.
- Allow a selected day/date range, with correct UTC query boundaries and DST handling.
- Show matching result totals, a sensible bounded page size/result-size choice, and a
  load-more/expand action rather than an unbounded query for every stored log.
- Keep category/outcome/search filters, including clear purchase and fulfillment filtering.
- Preserve filters while loading more and provide useful empty/loading states.
- Make B01's correlated payment and delivery outcomes readable in event details.

Dependencies: B01 for new commerce details; generic date/table work can be independent.

## B08 - P1 - Rework loading and progress feedback

Status: completed and merged in [PR #51](https://github.com/chenggggggg/iD-Develops/pull/51).
Public links use ordinary navigation without a floating progress popup. Portal links
replace only the content panel with a centered spinner and an accessible announcement.
Native action forms show progress on the clicked button, reuse existing spinners, and
preserve submitted button values while preventing duplicate posts. Checkout forms opt
into the shared feedback. Existing signup and booking indicators remain in place.
Cancellation, browser Back, retry after an error response, and reduced motion are covered
by browser regressions. Desktop/mobile loading views have been inspected. No database
migration or environment changes are required.
Release build, full formatting verification, all 487 unit/integration tests, all 205
Playwright tests (including 26 focused feedback cases), JavaScript syntax, and whitespace
checks passed on 2026-11-07.
CI follow-up: event-type editor controls wait for their JavaScript handlers before
accepting interaction. Delayed-script desktop/mobile regressions verify that editing
updates the selected record and preserves its credit policy and teacher assignment.
The existing create/edit workflow also checks the selected record ID before saving.
Follow-up validation passed on 2026-11-09: Release build, formatting verification,
all 487 unit/integration tests, all 207 E2E tests, JavaScript syntax, and whitespace.

**Estimated size: Medium.** Shared progress components/scripts, portal navigation behavior, public navigation cleanup, and duplicate-submit/retry browser checks.

Previously saved loading-UI PR, expanded for all scheduling actions.

- Remove the global navigation loading popup on public pages.
- Actions show a spinner on the responsible button/panel and prevent duplicate requests.
- Portal navigation keeps the left navigation visible while the content panel shows a
  small centered spinner and accessible progress feedback.
- Restore controls after failure and preserve validation/retry behavior.
- Cover recurring-class saves, appointment booking, checkout, and other slow submissions.

Dependencies: coordinate shared component conventions with B12 without blocking urgent fixes.

## B09 - P1 - Signup steps and contact notifications

Separate follow-up requested 2026-11-21 (Small): add Reply-To to the company contact
notification so staff can reply directly to the visitor. Clarified 2026-11-23: the
visitor confirmation comes from Mail:NoReplyEmail without Reply-To and is separate from
the conversation. The visitor first sees that conversation when the company replies;
subsequent company/visitor replies use their normal email clients. Preserve both sends.
Follow-up status: implemented and verified on fix/contact-email-reply-to, 2026-11-23;
ready for PR review. Reuses the existing mail service and both providers. Preserves the company
sender, localized feedback, validation, and error handling; confirmation uses the existing
configured no-reply address with no Reply-To header or links to the conversation.
Verification after clarification: Release build, full formatting, all 698 unit/integration
tests, all three Full-mode contact browser checks (English/Dutch sends and validation),
and all 135 CI-selected Marketing browser checks passed. Tests check visitor Reply-To
only on the company notification, no-reply confirmation sender with no Reply-To, default
omission, and stable retry bodies/keys. Resend HTTP was mocked; browser mail used File.
No real emails were sent. Original local settings and Full mode were restored. No
migration or new environment/provider/DNS configuration is required.

CI follow-up on the same open PR #72, 2026-11-23: run 45110906763 timed out before the
external-return-URL assertion. Its trace shows `/login` HTTP 200 in about 6 ms and all
first-party resources loaded; the synchronous `consent.cookiebot.com/uc.js` request never
finished. Isolate the external SDK in CI and local redirect tests without changing real
authentication or redirect assertions. Verified: Release build, formatting verification,
all 698 unit/integration tests, 80 deployment-policy checks, three consecutive 13-case
login regression runs, all 329 Full-mode and 135 Marketing browser checks. Production
consent loading is unchanged. Original local settings were restored. Review this
external startup dependency separately in the B20
runtime/UI audit before proposing any loader change that could affect consent blocking.

**Estimated size: Large.** Expanded signup workflow with built-in personal details and mandatory notifications.

Status: completed and merged in [PR #40](https://github.com/chenggggggg/iD-Develops/pull/40).

- Product details -> required full name/email -> custom questions -> confirmation screen.
- Phone, street address, postal code, city, and country are optional built-in fields.
- Save the submission and queue both customer and company notifications atomically;
  include contact information explicitly in the company notification.
- Preserve legacy submission data, protect against duplicate submissions, and share the
  signup flow with Portal previews while disabling preview submission.
- Preserve localized public styling and accessible validation, navigation, and focus.

B14's editable product button labels remain a separate planned PR.

## B10 - P0 - Exam results and completion-page refresh

Status: merged as PR #52 on 2026-11-10.
Results use the attempt's saved grade settings, version, time limit, feedback, questions,
answers, and media. Shared grading presentation keeps result emails consistent and replaces
the fixed 60-point language-level assumptions. The responsive review uses native keyboard-
accessible disclosures and explicit unanswered/empty/unavailable states. Completion pages
share a localized layout, visible form labels, and the restored desktop image panel.
The original 1920x1080 JPEG was converted to WebP (191,083 to 152,402 bytes).
Release build, formatting verification, all 500 unit/integration tests, and all 216 E2E
tests passed on 2026-11-09. The final spacing polish was rechecked with the full unit suite
and all 15 affected exam browser tests. English/Dutch desktop/mobile screenshots were
inspected. No migration or environment-variable changes are required.
Launch follow-up completed on 2026-11-10: public result/completion responses disable
caching and request exclusion from search indexing; protected result links use a
no-referrer policy. Browser tests cover these headers and invalid/mismatched links.
Full-suite verification exposed a consent-delayed Portal dialog initialization race:
essential layout behavior now initializes independently of consent, and exam Start
buttons wait for their launch handler while preserving locked-exam restrictions.
Both delayed-script regressions and the original exam-start/refund concurrency test pass.
Final verification: Release build, formatting, all 500 unit/integration tests, and all
218 E2E tests passed. Commit, push, and PR creation authorized on 2026-11-10.

**Estimated size: Medium.** Results layout/data presentation plus completion imagery and responsive checks; reuse the existing immutable exam data.

Previously saved examination PR; includes relevant image conversion from the new request.

- Replace the outdated level-test results presentation using the immutable attempt/version
  data, showing the actual exam taken and its resulting feedback.
- Restore the left completion panel using exam-completed-wallpaper.webp converted from the
  existing JPEG; verify mobile behavior and asset loading.
- Keep examination layout identity while aligning common typography and controls with B12.

Dependencies: existing preserved exam versions. Completion wallpaper does not require the
new homepage hero asset and is not blocked by B15.

## B11 - P1 - Calendar/roster workflow and layout redesign

Status: merged as PR #50 from `feature/calendar-roster-ux` on 2026-11-07.
Uses Tailwind utilities for balanced lists/editors, readable forms, grouped advanced
settings, working hours, and date-aware CET/CEST labels. Save failures retain submitted
values and validation errors. Handler-specific form binding prevents unused forms from
blocking saves. Student calendar tabs support keyboard navigation after initialization.
Release build, full formatting verification, all 487 unit/integration tests, all 198
Playwright tests, JavaScript syntax, and whitespace checks passed on 2026-11-07.
Desktop/mobile views and winter/summer/DST-transition labels were inspected. No database
migration or environment changes are required. Local provider fixtures use in-person
delivery or pending Zoom setup rather than creating real external meetings.
Follow-up: the current scheduling section is a non-interactive selected indicator;
only the other sections are links. Repeated-click checks across all three sections
passed in six desktop/mobile navigation regressions. Build and formatting checks passed.
Reliability follow-up: editor/menu controls wait for initialization, including late
public-menu scripts. Credit-policy validation has visible field feedback and separate
client/server regressions. Identity validation loads working local scripts after jQuery.
Snapshot transaction cleanup preserves the original database error for retries.
Browser traces and request IDs support failure diagnosis; two E2E collection workers
avoid overloading the shared app while keeping concurrency scenarios inside tests.

**Estimated size: Large.** Restructure several calendar management workflows across roles and screen sizes while preserving complex scheduling behavior.

New focused scheduling UX PR, incorporating the earlier timezone-label request.

The B05 implementation already separates scheduling from availability and provides
role-appropriate Calendar actions. Continue from that structure rather than duplicating
the page split; the broader list/form layout, compact inputs, and timezone polish remain here.

- Prefer task-oriented sections/tabs: Scheduled/recurring classes, Appointments/Event types,
  and Teacher availability. For admins, make upcoming classes and primary create actions
  prominent rather than always placing the weekly availability form first.
- Teachers can have a role-appropriate entry point to their schedule and availability;
  students see booking and their appointments without management forms.
- Give lists and creation forms balanced usable widths, readable text, and responsive
  tables/cards. Avoid the current wide list beside a cramped form.
- Keep essential teacher/date/time/repeat inputs visible; place infrequent options under
  Advanced options with clear validation and preserved values.
- Show teacher/provider readiness and pending setup next to the relevant schedule item.
- Display Amsterdam with CET or CEST for the date being shown, rather than hardcoding CEST
  year-round. Include daylight-saving transitions in verification.

Dependencies: use B02/B04/B05 workflows as available; small independent layout fixes can
be delivered first. Reuse B08 progress indicators and B12 shared controls.

## B12 - P2 - Shared website identity across Public, Portal, Examination

**Estimated size: Large.** Application-wide theme/semantic discovery and shared-component standardization; deliver implementation as smaller coherent PRs rather than one broad rewrite.

Previously saved audit/standardization initiative, to be split into component-sized fixes
based on evidence from the audit rather than one application-wide rewrite.

- One recognizable identity: shared font families, type scale, button geometry, spacing,
  sizing, icon placement, and interaction behavior, with environment palettes/layouts.
- Audit anchors for navigation and buttons for actions, including navigation subpatterns.
- Review unstyled functional controls, palette usage, focus/disabled/loading states,
  accessibility, responsive layouts, and justified specialized components.
- Standardize transient save/sync feedback, centered process feedback dialogs, anchored
  validation, and important confirmations by purpose. Remove unintended browser alert,
  confirm, and prompt implementations without sacrificing accessible interactions.
- Keep intentional examination/public interfaces and shared component reuse.
- Review public font delivery while preserving the existing families. B24/B25 verification
  reproduced delayed rendering when an external font stylesheet remained pending with
  JavaScript disabled; consider self-hosting licensed font assets rather than relying on
  third-party stylesheet availability. Keep this separate from purely cosmetic changes.

Dependencies: evidence-backed theme/behavior baselines; urgent functional fixes need not wait.

## B13 - P2 - Remaining exam form and pagination consistency

**Estimated size: Small.** Focused existing exam control/pagination corrections and regression checks; no exam lifecycle redesign.

Previously saved exam UI PR, avoiding duplicate work already merged in #11/#28/#29.

- Verify create/edit/settings hh:mm:ss parsing and presentation; correct remaining gaps.
- Align time-limit input width, helper-text positioning, and spacing.
- Remove the circle around editor question pagination numbers and align text/selected states.
- Preserve timing/version semantics and persistent media-save warnings.

## B14 - P2 - Product-editor CTA controls and advanced settings

**Estimated size: Medium.** Persisted/localized CTA labels, editor interactions, preview/public icon behavior, advanced settings placement, and tests.

Previously saved product editor PR.

- Move CTA icon to the right of its label.
- In edit mode, show a pencil icon and allow editing the CTA label by interacting with it.
- Preview/public view uses the arrow icon; labels remain editable and localized appropriately.
- Move slug configuration under Advanced settings; preserve automatic slug updates and
  redirects from previous product URLs already implemented in #35.
- Coordinate labels with B09's initial signup action and final form action.

## B15 - P1 - Brighter homepage hero and public-image optimization

**Estimated size: Medium.** The hero replacement alone is Small; the full raster inventory, conversions, responsive assets, cache/reference checks, and image quality verification are broader.

Homepage replacement verified on `design/brighter-home-hero`, using the owner's
`group-of-businesspeople-in-a-meeting.webp` (1123x750, about 63 KB), supplied 2026-11-16.
This focused batch preserves the existing copy and actions, adds prioritized image
loading, and uses the public navy/wine palette over a light cream overlay. Static
previews and live English/Dutch desktop, tablet, and mobile renders were inspected,
including 320px layout clearance and the localized level-test/contact links.
Tailwind build, Release build, solution formatting, all 603 unit/integration tests,
242 Full-mode E2E tests, and 42 Marketing-mode E2E tests pass. Public smoke checks
also passed for 26 localized pages and 14 images, including the supplied hero asset.
The original local settings were restored after checking Marketing mode.
The remaining asset batch is implemented and verified on `perf/public-image-optimization`,
approved 2026-11-19; merged in PR #64. The homepage team carousel reuses the
existing responsive founder portraits. Six error illustrations and the slideshow fallback
use smaller WebP versions without changing their content; illustration transparency is
unchanged. Removed 24 retired assets with no source or local stored-content references
and eight replaced originals. Retained image assets total 1,305,392 bytes instead of
32,067,880 bytes. PNG email branding and active logos remain intact.
Verification: Release build, solution formatting, all 615 unit/integration tests,
54 relevant Full-mode browser tests, 74 Marketing-mode browser tests, and 75 deployment
policy tests passed. All 25 retained images served correctly and returned HTTP 304 for
matching ETags. Release smoke checks passed for 26 localized pages and nine shared images.
Desktop/mobile team and error-page renders and original/optimized image comparisons were
inspected. Physical-device and staging stored-content inspection were not performed.

- Replace the current dark hero with the supplied brighter image; tune crop and overlay
  without losing readable text. Do not select/generate the replacement on the user's behalf.
- Audit relevant public raster images and convert suitable photos/backgrounds to optimized
  WebP with appropriately sized responsive variants. Do not convert SVG logos/icons or
  blindly replace already optimized assets.
- Check dimensions, layout stability, loading priority, caching, and image references.
- Recommended storage policy: deploy stable theme assets in wwwroot/images; use existing
  Cloudflare media storage for administrator-managed blog/product/content uploads.
- R2 public media should use a production custom domain and cache configuration. R2
  storage itself does not automatically resize or convert images; design that upload or
  image-transformation pipeline deliberately. Do not expose private attachments as media.
- Avoid a bulk asset migration solely to move files off the application server.

Dependencies: supplied hero received; align reusable media handling with B16/B17. B10 handles
its completion wallpaper independently.

## B16 - P2 - Blog/news foundation and admin management

**Estimated size: Large.** New content models/migrations, public routes, admin management, publication permissions, media, and browser coverage.

New feature foundation PR; Shopify /blog is a structural reference, preserving this site's identity.

- Public /blog index and article routes, integrated with existing culture routing.
- Admin/SuperAdmin-only Blog navigation and CRUD; teachers/students have no authoring access.
- Blog/news post type, title, slug, excerpt, cover image, draft/published state, and metadata.
- Derive author from the authenticated creator, with publication/update metadata and
  appropriate permissions. Keep unpublished content out of public queries.
- Media uses the existing public upload/storage architecture and supports optimized assets.
- Provide a usable initial article authoring/viewing path; the advanced block editor follows
  in B17 without replacing content with an incompatible model.

## B17 - P2 - Preview-style article block editor and hierarchical contents

**Estimated size: Extra large.** A structured visual editor, multiple block types, shared rendering, accessible ordering, content validation, and hierarchical TOCs. Split into editor foundation, blocks, and contents/preview sub-PRs before implementation.

Separate editor PR to keep the large blog feature reviewable.

- Article-page preview canvas with an empty dashed insertion area and centered plus button.
- Add/reorder/edit/remove hero, rich text/headings, image, YouTube, and CTA blocks.
- Use a versioned structured content schema; validate/sanitize permitted rich text, media,
  links, and embeds rather than allowing arbitrary scripts or unrestricted HTML.
- Top-level section headings populate the main 'On this page' sidebar. Subsection headings
  can populate a local contents list inside their section without cluttering the sidebar.
- Generate stable unique anchors and accessible heading order; responsive contents navigation.
- Preview reflects public rendering; editor controls and draft content remain staff-only.
- Author information is generated automatically, not manually rebuilt inside each article.

Dependencies: B16; shared blocks should render consistently in editor and public views.

## B18 - P2 - Blog page curation and homepage news feed

**Estimated size: Medium.** Editorial layout configuration, post ordering/featured selections, responsive previews, and a homepage feed on the B16/B17 foundation.

Separate editorial presentation PR.

- Admin manages article placement, featured posts, and ordering on the blog landing page.
- Support intentional card/grid and list-style preview layouts with appropriate image,
  excerpt, and metadata behavior, inspired by Shopify's mixed editorial presentation.
- Add a published blog/news feed near the bottom of the homepage.
- Keep publication state, ordering, pagination, responsive behavior, and empty states clear.
- Limit configuration to useful layout choices and preserve the site's shared identity.

Dependencies: B16 and coordination with B17; B15 supplies reusable media conventions.

## B19 - P2 - Investigate EF multiple-collection query warning

**Estimated size: Medium.** Investigation and measurement precede a focused query correction; estimate is provisional until the offending query is identified.

Previously deferred performance PR.

- Identify the actual multi-collection query behind the EF warning and measure its impact.
- Apply a targeted projection or split-query approach where justified; do not globally
  switch query behavior without considering consistency and database round trips.
- Verify affected workflows and query/result behavior.

## B20 - P2 - Final page-by-page UI/UX improvement audit using installed skills

**Estimated size: Large (audit effort).** This covers investigation and reporting; the
size of implementation PRs depends on the confirmed findings.

This is the last planned audit milestone after the preceding UI/features are available.
B12 establishes the shared identity and component conventions; B20 evaluates practical
usability, polish, and regressions across the resulting application, including new pages.
Reuse B12's baselines and existing finding IDs rather than report the same issue twice.
A newly found functional/accessibility blocker should be escalated immediately, not left
until optional cosmetic work is complete.

Skill references for execution:

- web-design-guidelines: semantic controls, accessibility, forms, navigation, loading,
  typography, performance, and established web interface guidance. Fetch the skill's
  current upstream guidelines when the audit is actually performed.
- emil-design-eng: hierarchy, visual balance, readable typography, spacing, interaction
  feedback, appropriate density, and component polish while preserving the site's identity.
- mobile-native: touch behavior, mobile forms/keyboards, viewport/safe-area handling,
  navigation, scrolling, and layout behavior. Device emulation is not physical-device
  verification; disclose real-hardware checks that remain unavailable.
- review-animations: purpose, frequency, timing, interruption, reduced motion, and animation
  performance for existing motion. Do not add motion just because a skill mentions it.

Scope:

- Inventory Public, Portal, and Examination routes, shared components, and role-dependent
  interfaces for anonymous users, students, teachers, admins, and superadmins.
- Inspect hierarchy, primary actions, button/anchor semantics, forms, field guidance,
  feedback/popups, tables, pagination, navigation, empty/loading/error/disabled states,
  readability, responsive layouts, keyboard use, focus, contrast, and touch targets.
- Include new blog/article/editor pages, event types, booking and roster flows, and
  purchased-exam access, rather than limiting coverage to today's existing pages.
- Preserve shared website identity, environment palettes/layouts, purposeful custom UI,
  and existing functional behavior. Respect Tailwind-first project conventions.
- Inspect source and verify safely in the already-running app where available. Do not
  submit business forms, change records, upload files, trigger payments/emails, restart
  the server, install packages, or make application code changes during the audit.

Deliverables and boundaries:

- A coverage inventory, with inaccessible roles/pages/states explicitly listed.
- Prioritized findings with actual page/component location and file/line evidence, user
  impact, root owner, proposed before/after improvement and reason, verification status,
  approximate fix size, dependencies, and regression risk.
- Distinguish confirmed defects and accessibility problems from optional design advice;
  use visually verified/source verified/needs verification labels accurately.
- A report in the conversation first, following applicable skill review formats. Any
  corrective implementation is proposed as separate, reviewable PRs for explicit approval.
  Do not silently turn the audit into an application-wide redesign or automatic fixes.
- This backlog entry reserves audit work; it does not require opening an empty GitHub PR
  before findings or a concrete reviewable change exist.

Dependencies: use completed B12 conventions and inspect other delivered backlog features;
record any preceding items still incomplete rather than claiming final complete coverage.

## B21 - P1 - Payment history and refunds for unused purchases

Status: completed and merged in [PR #47](https://github.com/chenggggggg/iD-Develops/pull/47).
Build, formatting, all 453 unit/integration tests, all 163 Playwright tests, and EF
model/migration consistency passed on 2026-11-04. Desktop and mobile purchase/refund
screens were inspected. Provider verification used local/fake refunds; run a Stripe
test-mode smoke check after deployment and webhook setup.

Self-service eligibility is limited to reliably tracked, entirely unused exam/credit
purchases. Courses, external services, guest purchases, and incomplete historical
snapshots require company review. Enable the four refund webhook events documented
in `iD-Develops/ENVIRONMENTS.md`; no new DigitalOcean variables are required.

**Estimated size: Large.** Account payment history, purchase-specific usage checks,
monetary refunds, payment/provider reconciliation, entitlement reversal, concurrency,
and relevant unit/integration and browser coverage.

New separate feature PR, requested on 2026-11-02. This is saved scope, not an instruction
to implement it before B06 or to change legal terms.

- Add a Payment/Purchase History section to My Profile showing the signed-in user's
  purchases, purchase date, product, paid amount/currency, payment/refund status, and
  a useful order reference. Enforce ownership on the server; do not attach historical
  purchases to an account solely because an email address matches.
- Offer a Refund button for an otherwise eligible paid, unused purchase. Explain the
  amount and consequences before confirmation, and show pending/success/failure states.
- Initial user-approved rule: once a purchased product has been used, disable or hide
  its Refund button and explain "This purchase has been used. Please contact the company
  to request a refund." Do not prevent a separate request to the company.
- For exams, creating an attempt record from the purchased allowance means it has been
  used, even if the attempt is unfinished or later cancelled. Link usage to the purchase
  rather than treating unrelated earlier exam attempts as use of a new purchase.
- Apply the same unused/used distinction across product types. Credit reservations or
  consumption, course usage, and delivered/consumed services need reliable purchase-linked
  evidence. Define the concrete usage signals for each type during investigation; do not
  expose self-service refunds where unused status cannot be reliably established.
- Initial self-service scope: full refund of an entirely unused purchase. If any part of
  a bundle or allowance has been used, direct the customer to the company. Partial refunds
  and exceptional used-product refunds remain manual rather than guessing a company policy.
- Perform eligibility checks on the server immediately before processing. Coordinate
  refund processing with exam starts, credit use, and other consumption so simultaneous
  requests cannot both use and refund the same purchased benefit.
- Integrate monetary refunds with Stripe using persisted, idempotent refund requests and
  verified provider results/events. Handle retries, duplicate events, pending outcomes,
  provider failures, and refunds performed externally without issuing duplicate refunds
  or claiming success before confirmation.
- Revoke only the refunded purchase's unused entitlements as part of a consistent refund
  lifecycle; preserve other purchases, independent assignments, exam records/results,
  financial history, and audit evidence. Record refund requests and outcomes through B01.
- Keep the eligibility rule configurable/extendable for the company's later refund-policy
  decision. Do not invent a time window, deductions, or update Terms & Conditions in this PR.

Dependencies: B01 audit trail and reliable purchase/fulfillment identity; B06's purchased
exam allowances and version/record links. Broader policy and legal-text changes are deferred
until the company confirms them. Include My Profile/history/refund states in B20's final audit.

## Decisions and dependencies retained

- Making Exam Level optional remains a separate pending product decision, not an approved
  implementation requirement or an accidentally forgotten PR.
- B03's Zoom organization/licensing/hosting choice needs confirmation before architecture work.
- B02's staff-only pending schedules and provision-after-connection behavior were approved
  and delivered in PR #42.
- B04/B05 must preserve staff ownership rules and define on-behalf booking and credit ownership.
- B06: product editors can configure Attempts on purchase, defaulting to the selected
  exam's allowance. Quantity one and additive repeat purchases were approved on 2026-11-02.
  Completed and merged in [PR #44](https://github.com/chenggggggg/iD-Develops/pull/44).
  Verification: Release build, formatting, all 415 unit/integration tests, 44 relevant
  browser tests, and EF model/migration consistency passed on 2026-11-02.
- B21: unused purchases may use the refund button; used purchases require contacting
  the company. Defer additional refund rules and legal wording until company approval.
- B15's supplied replacement hero is merged in PR #61; the remaining image optimization
  and cleanup are merged in PR #64.

## Suggested implementation order

The 2026-11-10 marketing launch order supersedes the earlier feature-completion sequence:
B10 -> B22 -> B23 -> B24 -> B25 -> B26 -> B27. Run useful B15 asset work alongside these.
Existing completed functionality stays available to staff through the portal.
After launch, resolve B03's Zoom decision, continue the broader B12 identity work and
B13/B14 polish, and deliver blog work in the B16 -> B17 -> B18 sequence. B19 remains a
targeted performance investigation. B20 remains the final broader UI/UX audit.

## B22 - P0 - Exam-taking layout and header clearance

Status: completed and merged in [PR #53](https://github.com/chenggggggg/iD-Develops/pull/53).
Public and Portal taking pages share a left-aligned, narrower answer area, with context/media
first and the question immediately above its answers. Choice controls use existing brand
tokens, visible keyboard focus, and native radio behavior. Open answers have an accessible
question label. The taking header stays sticky in normal flow, wraps long titles, and no
longer covers content. Taking pages override inherited viewport-height/overflow constraints
so long questions scroll normally. Editor/start-page presentation remains outside this batch.
All styling changes use Tailwind utilities; no custom CSS or migrations were added.
Verification: Release build, formatting, all 500 unit/integration tests, and all 224 E2E tests
passed. Six new public/Portal scenarios cover 320/390/768/1440px widths, English/Dutch,
keyboard selection/deselection, focus, help, media controls, saved answers, navigation, reload,
and scrolling. Screenshots were inspected. The initial concurrent unit/format run hit three
database-startup timeouts; the unchanged full unit suite passed when run on its own.

P0 marketing-launch mobile follow-up completed and merged in [PR #67](https://github.com/chenggggggg/iD-Develops/pull/67), 2026-11-21. Production deployment/acceptance is still pending at this review; B34 tracks remaining desktop drag/scroll interaction work.

- Review and optimise examination visuals on phones and tablets, including portrait/landscape
  layout, question/context/answer spacing, media, timer/header, pagination, and submit controls.
- Include the level-test completion form and result/review screens where their mobile layout
  needs improvement. Preserve autosave, timing, answer state, submission, and result access.
- Verify touch targets, keyboard/focus, scrolling, and available browser engines; document
  physical-device checks separately from emulation.

Implemented: compact timer/title header; horizontally scrollable question numbers with
an always-visible Submit action and question count; bounded question media; safe-area
spacing; readable mobile completion/result views; compact completion-form Turnstile;
and a localized native submission dialog with scrollable unanswered questions,
keyboard containment, cancellation, and focus restoration. Custom radio labels retain
a visible focus outline in Chromium and WebKit. No exam data model or migration changes.

Verification: Release build and formatting; all 615 unit/integration tests; 22 final
Chromium and 22 WebKit exam layout/workflow checks; all 95 Marketing-mode E2E checks;
and 75 workflow-policy tests passed. Screenshots were inspected. The new 30-question
regression reproduced inaccessible navigation before the fix. WebKit additionally exposed
missing radio focus styling, which was corrected without dropping the outline assertion.
Original local configuration was restored and the existing app is running in Full mode.
Physical-device testing was unavailable; browser viewport checks are not hardware checks.

**Estimated size: Medium.** Focused taking-surface layout and responsive/browser regressions.

- Give scenario/context, question text, and answers one consistent left edge.
- Lay out context and optional media first, with the question immediately above its answers.
- Left-align multiple-choice labels and text. Slightly narrow the answer card while keeping
  long content, images, open answers, and touch controls usable.
- Account for the fixed examination header at desktop/mobile sizes so it cannot cover content.
- Preserve answer autosaving, question navigation, keyboard selection, timing, and submission.
- Keep unrelated editor pagination/input polish in B13.

## B23 - P0 - Reversible Marketing/Full access policy and navigation

Status: completed and merged in [PR #54](https://github.com/chenggggggg/iD-Develops/pull/54) on 2026-11-12. Release build,
full formatting verification, all 579 unit/integration tests, 226 Full-mode Playwright tests, 34 Marketing-mode
Playwright tests, 36 registry/workflow regression tests, and CI lifecycle Bash syntax
checks passed. Marketing checks cover English/Dutch desktop/mobile navigation, staff
management, customer access rejection, contact receipts, and anonymous protected results.
The existing local watcher was restored to Full mode. No database migration is required.
Production promotion (B27) and mandatory Full-launch account association (B28) remain pending.
CI run #116 follow-up: recurring-editor script interception now waits for running route
handlers during cleanup, preventing "Route is already handled!" from leaving Edit disabled.
All keyboard/value-preservation assertions remain; five repeated desktop/mobile runs
(10 cases) passed before the complete combined-branch browser suite.

**Estimated size: Large.** Central release policy, authentication/access boundaries, public UI, and both-mode regressions.

- Introduce the server-side SiteMode=Marketing|Full setting with validated configuration.
  Production/default configuration is Marketing; Local/Development/Staging default to Full.
- Keep navigation/components in place and conditionally hide public Login/Portal links.
- Marketing blocks public registration and external account creation, customer/teacher login,
  existing customer sessions, customer Portal access, and new public signup/checkout/booking.
- Preserve Admin/SuperAdmin sign-in, two-factor authentication, account recovery, and management.
- Show public product information and contact actions instead of transaction forms.
- Preserve contact, anonymous level-test completion/email/protected results, signed provider
  callbacks, and health probes. Public exam routes must reject non-level-test attempts.
- Test Marketing and Full independently in the same required CI workflow; preserve existing
  Full-mode authorization and data. Switching modes never deletes accounts or exam records.
- B28 supplies mandatory account-linked results for the completed Full release; B23 alone
  does not certify Full launch readiness. B27 implements production promotion separately.

## B24 - P0 - Public marketing design foundations

Status: completed with B25 and merged in [PR #56](https://github.com/chenggggggg/iD-Develops/pull/56).
Shared hero, action, offering-list, and contact partials establish
consistent public typography, widths, section spacing, keyboard focus, and responsive
behavior. Public primary actions use the existing wine-red accent already used by the
navbar and public links; Portal and Examination retain their own palette ownership.
These components are available for B26 without redesigning its page in this PR.

**Estimated size: Medium.** Bring forward the public-only part of B12 for launch.

- Establish evidence-backed public conventions from the existing homepage/layout and tokens.
- Apply shared typography, button geometry, spacing, content widths, form/focus states,
  and responsive patterns needed by B25/B26.
- Preserve recognizable website branding and the examination palette/structure.
- Use Tailwind and existing reusable patterns; scope changes to actual launch needs.

## B25 - P0 - Public service-page design and information

Status: initial four-page batch merged in [PR #56](https://github.com/chenggggggg/iD-Develops/pull/56).
The four service routes now use photo-led introductions, clearly grouped
content, numbered offering lists, and prominent existing next steps. All original rendered
English/Dutch text blocks were compared against the running redesigned pages: none are
missing. Existing localized offer differences and consultation destinations are preserved.
Eight new browser tests cover 320/390/768/1440px, both languages, keyboard navigation,
image loading, header clearance, overflow, and contact/level-test links without JavaScript.
Responsive WebP variants reuse the existing photographs (13-76 KB each).
Original assets were retained for B15's usage audit; that cleanup is now implemented
on `perf/public-image-optimization` after source and local stored-content checks.
PR #56 verification: Release build, solution formatting check, all 579 unit/integration tests,
234 Full-mode browser tests, and 42 Marketing-mode browser tests passed. The eight new
service-page checks also passed separately after isolating the no-JavaScript fallback
checks from render-blocking third-party stylesheets. Desktop/mobile screenshots were
inspected. The local application configuration is restored to Full after mode checks.

Design follow-up (P0, Medium): `fix/nt2-personal-development-design` revises NT2 and
Personal Development in merged PR #57. The composition uses one shared outer grid with
deliberately positioned imagery, introductions, supporting statements, and body text.
Centered narrow wrappers are removed. Longer passages split at sentence boundaries;
opening sentences and the free introductory conversation receive appropriate emphasis.
All English/Dutch words, punctuation, and existing actions remain intact. The installed
Emil design-engineering, mobile-native, and web-design-guidelines skills inform hierarchy,
responsive reading order, semantic controls, and focus/touch behavior.
Browser regressions check common section edges, readable text, paragraph separation,
and non-overlapping columns. Nine new unit cases protect lossless paragraph splitting.
Desktop/mobile browser renders were inspected; physical-device testing is unavailable.
Verification passed: Release build, solution-wide formatting, all 588 unit/integration
tests, eight Full-mode service-page browser checks, and 42 Marketing-mode browser checks.
A separate comparison of all eight original localized page bodies found no missing text.
The local app is restored to Full mode after verification.

PR #57 final verification also covers shared typography and paragraph spacing across
all four service pages, the refined Personal Development hero, and the Dutch navigation
heading regression. Release build, solution formatting, 603 unit/integration tests, and
12 relevant Full-mode public browser checks passed before merge.

NT2 narrative refinement in the same PR follows learner goals -> teaching approach ->
course choices -> trainers -> testimonials where available -> next step. Company promises
use ordinary explanatory text; only customer testimonials use quote styling. Cultural
examples split at a translation-specific editorial boundary instead of a character midpoint.
The two main course routes remain visible; optional English support services use a labelled,
keyboard-accessible disclosure. Original copy is retained, with short localized topic labels.
MCP review covered English desktop and Dutch mobile. Browser checks protect chapter order,
trainer placement, quote roles, and access to every optional service. Build, formatting,
588 unit/integration tests, eight Full-mode checks, and 42 Marketing-mode checks pass.

**Estimated size: Large overall.** Delivered as one coherent four-page change using shared components.

- Completely redesign /enterprise, /nt2-integration, /personal-development, and
  /language-and-communication using B24's patterns.
- Preserve all current English/Dutch page text; change layout and visual presentation.
- Make the offer, intended audience, and next step clear through content hierarchy.
- Preserve English/Dutch localization and flag information requiring owner confirmation.
- Check mobile/desktop layouts and all advertised destinations, including visible links
  to service subpages. Additional unfinished destinations need an explicit scope decision.
- Keep working contact, consultation, and level-test paths prominent.

## B26 - P0 - About page and company information

**Estimated size: Medium.** Focused public page and content review.

Status: completed and merged in [PR #58](https://github.com/chenggggggg/iD-Develops/pull/58).
The About page shares the service pages' typography, outer
grid, paragraph spacing, and closing contact component. Separate founder biographies
and authored paragraph breaks preserve the existing English/Dutch wording. Responsive
WebP variants reuse the actual founder photo (25/52 KB); the original PNG remains for B15.
Release build, solution formatting, 603 unit/integration tests, 20 Full-mode public browser
checks, and 14 Marketing-mode checks pass. Marketing checks confirm account links are
absent from desktop/mobile navigation and direct Admin/SuperAdmin portal access works.
Desktop English and mobile Dutch renders have been inspected in the browser.

- Refresh /about using the public foundations and actual company information.
- Use existing approved imagery and clear contact paths; do not invent company claims.
- Verify localized copy, responsive layout, headings, links, and image presentation.

## B27 - P0 - Marketing production launch and acceptance checks

**Estimated size: Medium.** Environment configuration and complete public-flow verification.

Production preparation, 2026-11-19: the GitHub `production` environment is created
with a main-only deployment rule and seven non-secret release variables. No paid
production resources were created and DNS is unchanged. The current DigitalOcean token
cannot inspect Managed Databases; the existing staging database type/plan still needs
confirmation. A fresh production database requires reviewed content import and media
transfer; the existing exam importer creates a draft and retains media references.

Small CI follow-up implemented on `fix/no-script-language-switch-ci`: the merged #64
run failed the desktop no-JavaScript language-switch check before staging deployment.
Isolate that fallback-navigation test from third-party asset delivery and use the
tracked fixture context so failure diagnostics capture the actual page. Keep real
clicks, keyboard navigation, query preservation, and both language directions.
Verified locally: Release build, solution formatting, 615 unit/integration tests,
54 relevant Full-mode browser tests, 74 Marketing-mode browser tests, 75 deployment
policy tests, and the GET-only release smoke passed. Both moved no-JavaScript cases
executed in both modes. Original local settings and Full mode were restored. Awaiting
PR review and successful GitHub CI before production resource creation.

Status: implementation merged in [PR #60](https://github.com/chenggggggg/iD-Develops/pull/60). Production provisioning, DNS cutover,
and live acceptance remain separate from this implementation. The manual workflow
selects a successful main CI/staging run, requires executed Full/Marketing browser
checks, resolves its existing image digest, preserves the production app specification,
and pins only its image. It rejects the staging app ID, unintended sources/components,
Autodeploy, and incorrect Marketing/production runtime settings. No build, image push,
registry cleanup, or infrastructure creation occurs during promotion. GET-only smoke
checks cover localized public pages/media, health, and staff-only entry; full live email,
exam, result-link persistence, and backup checks are documented in the release runbook.
The recurring-editor timing follow-up is merged in [PR #59](https://github.com/chenggggggg/iD-Develops/pull/59).
Verification: 75 offline deployment-policy tests, two workflow YAML files and 28 Bash
steps, real successful GitHub CI metadata, Release build, solution formatting, all 603
unit/integration tests, all 242 Full-mode E2E tests, and all 42 Marketing-mode E2E tests
passed. The GET smoke checker also passed against the existing local Marketing app:
26 localized pages and 13 images. Exact original local settings were restored and the
existing watcher returned to Full mode. No production resources were created or changed;
the real provider rollout, production email/DNS, persistent-link redeployment check,
and live acceptance remain pending the owner's infrastructure setup and activation.

- Prepare production domains/configuration and separate production data from staging/test data.
- Keep protected-result signing keys persistent across deployments and confirm emailed links
  use the production public host and HTTPS.
- Verify production mail delivery, contact submission, level-test start/autosave/countdown/
  submit, completion, emailed results, and direct authorized staff portal access.
- Check advertised public routes, mobile layouts, broken media/links, public metadata,
  and private-result cache/indexing behavior.
- Confirm deployment and rollback steps before launch. Production activation is a separate
  deployment action, not implied by saving this backlog or implementing an earlier PR.
- Keep automatic main-to-staging deployment. Add a separate manually triggered production
  workflow that promotes an approved, tested existing image pinned by digest, with production
  autodeploy disabled. Further staging updates must not change the live release.
- Set production SiteMode=Marketing initially. Full launch requires an approved newer image,
  SiteMode=Full, B28 completion, and acceptance of all required customer workflows.
- Launch acceptance must confirm Log In/Portal links are absent from both desktop and
  mobile public navigation in Marketing mode, including for signed-in staff. Preserve
  authorized staff access through the direct portal address. Do not remove authentication
  code or Full-mode navigation: switching SiteMode back to Full must remain reversible.
- Retain the active and previous production images for rollback; separate production data,
  persistent signing keys, URLs, and service credentials from staging.
- Result-link expiry/reissue and a short /results alias remain separate decisions.

## B28 - P1 - Verified account-linked level-test results for Full launch

**Estimated size: Medium-Large.** Required before production enables the completed Full experience.

- Keep test-taking available before registration. Full-mode result viewing requires sign-in
  or verified registration and secure attachment of the attempt to that account.
- Prove email/account ownership before linking; entering an existing account email is insufficient.
- Keep result presentation and immutable attempt data shared with Marketing mode.
- Preserve existing Marketing result links and account associations; explicitly decide/persist
  the access policy applying to each attempt so mode changes do not disrupt ongoing tests.
- Marketing mode continues to disallow public registration/login. Do not create accounts
  automatically for Marketing visitors or remove existing records when switching modes.

## B29 - P1 - Preview-only products and navbar language switch

**Estimated size: Medium.** Product setting, server-side action guards, localization,
and responsive public navigation with browser coverage.

Status: merged in PR #62, 2026-11-18.

- Add a per-product Preview only setting, independent of publication status and the
  site-wide Marketing/Full mode. Keep product information visible while hiding
  checkout/signup actions and rejecting new submissions server-side.
- Preserve existing product behavior by default and already-paid fulfillment.
- Add a visible globe/EN-or-NL switch in the public navbar, including mobile. Switch
  directly to the other language without opening a dropdown; retain page/query/fragment
  where applicable and use a usable navigation link when JavaScript is unavailable.

## B30 - P1 - Homepage service-section loading and scroll performance

**Estimated size: Medium.** Separate performance PR requested
2026-11-18; do not implement as part of B29.

Status: completed and merged in [PR #63](https://github.com/chenggggggg/iD-Develops/pull/63), 2026-11-19.

- Investigate the visible three-stage loading of Home > What we do: section title,
  bento/card surfaces, then their images. Identify image request timing, decoding,
  reveal triggers, animation scheduling, and layout/paint costs before selecting fixes.
- Bring relevant image readiness and card presentation together so entering the
  section does not expose a conspicuous incomplete intermediate state.
- Keep a coordinated first-entry fade for the heading and cards, with immediate
  visibility for reduced motion, keyboard focus, and unavailable scripting.
- Use the existing shared CSS fade-and-rise and scroll observer; do not introduce
  a service-specific animation API or timing system.
- Verify normal wheel scrolling, refreshes at the heading or lower cards, and fast
  jumps into the section so the reveal does not finish too early or leave content hidden.
- Keep the cookie-free reveal script executable while consent is pending or blocked;
  verify with Cookiebot active in Brave as well as deterministic consent-gate regressions.
- Profile the reported scroll stutter on desktop and mobile; check hover transforms,
  shadows, gradients, and animation properties without assuming the cause.
- Preserve the approved content/layout, reduced-motion behavior, keyboard access,
  and the homepage hero's loading priority. Avoid eagerly loading all below-fold
  media unless measurements justify it.
- Verify cold and warm loads, constrained network/CPU conditions, section entry,
  repeated scrolling, and available desktop/mobile browser traces. Report physical
  device verification limitations rather than treating emulation as real hardware.
- Update meaningful browser regressions where behavior changes; do not preserve a
  harmful loading strategy merely because an existing test asserts its attributes.

## B31 - P0 - Enable eligible public product signup and booking in Marketing mode

**Estimated size: Medium.** Product-type policy and coordinated public/server guards.
Status: completed and merged in PR #68, 2026-11-22 (667beb33c1f878b58984c4e72699c41d573725a9).

- Update the earlier B23 blanket transaction restriction to the new approved business
  requirement: public signup and Koalendar booking work for eligible active products
  when the product-level Preview only checkbox is off.
- Keep customer registration/sign-in and customer Portal availability independently
  controlled. Do not expose the complete unfinished Portal to enable a public form/link.
- Audit Signup, external booking/Koalendar, and other product workflow/CTA combinations.
  Make the policy for account-dependent paid/course/credit/exam checkout explicit;
  confirm any additional customer-account requirement before expanding that scope.
- Distinguish the persisted IsPreviewOnly product flag from the staff editor Preview
  tab. Preserve publication, sales-active, access-token, and authorization rules.
- Coordinate Product.cshtml, ProductModel handlers, SiteAccessPolicy,
  SiteAccessMiddleware, and downstream action services so a visible CTA can actually
  complete its permitted workflow. Hiding controls alone must not be the only guard.
- Preview-only products remain information-only and reject direct action requests.
  Preserve already-paid fulfillment, signed callbacks, contact, and public level testing.
- Preserve the existing personal-details-first Signup flow, required name/email,
  optional phone/address fields, confirmation screen, and mandatory notification emails.
- Verify Marketing and Full, preview-only on/off, active/paused products, anonymous/staff
  visitors, signup success/failure, external booking links, and direct POST bypasses.

Verification: Release build and format verification passed; 671 unit/integration tests,
45 affected Full-mode browser checks, 114 complete Marketing-selection browser checks,
and 75 deployment-policy tests passed. Original local settings were restored to Full.
No database migration or new application setting is required. Decorative CTA icons are
hidden from accessible names. Stripe/account-dependent actions stay restricted in Marketing;
already-paid fulfillment and signed callbacks retain their existing paths.

## B32 - P0 - Anonymous Portal login redirect with safe return destination

**Estimated size: Small.** Focused authentication/middleware correction.
Status: completed and merged in [PR #69](https://github.com/chenggggggg/iD-Develops/pull/69), 2026-11-22. Production promotion/acceptance is separate.

- Anonymous browser visits to the Portal root and protected deep links should challenge
  authentication and redirect to /login with the original local path/query as returnUrl.
- After an allowed user signs in, return to that destination using the existing safe
  local-redirect logic. Keep the staff-only Marketing policy and normal Full permissions.
- Authenticated users without permission should receive Access denied/403 rather than
  a login loop. Preserve Identity login, 2FA, recovery, logout, and anonymous public exams.
- Preserve appropriate API/fetch 401/403 responses instead of returning an HTML login page.
- Root cause: the Marketing Portal branch in SiteAccessMiddleware unconditionally sends
  403 for non-staff before the configured Identity login challenge can execute.
- Verify root/deep-link navigation, query preservation, successful staff return, forbidden
  customer access in Marketing, Full-mode access, and rejection of external return URLs.

Verification: Release build and formatting passed; 685 unit/integration tests, 46 affected
Full-mode browser checks, 127 complete Marketing-selection browser checks, and 75
deployment-policy tests passed. Original local settings were restored to Full. Browser
checks include root/deep-link/query preservation, staff return after sign-in, plain fetch/
XHR/JSON 401s, authenticated forbidden pages, and rejection of external return URLs.
Customer login, registration, and Portal role restrictions are unchanged. Cleared/invalid
customer sessions now follow the anonymous login challenge. No migration/settings needed.

## B33 - P0 - Single configured SuperAdmin startup owner

**Estimated size: Small-Medium.** Owner seeding, CI/release configuration, safe migration,
and security regressions. Expanded from the configuration-only task by the user's
2026-11-22 clarification: the startup account must control everything; ordinary Admin
accounts are assigned by the owner through user management, not automatically seeded.
Status: code completed and merged in [PR #71](https://github.com/chenggggggg/iD-Develops/pull/71), 2026-11-23.
Hosted owner configuration and production acceptance remain pending.

- Use only SuperAdmin__Email/SuperAdmin__Password for owner setup in every environment.
  Remove automatic Admin creation. Keep all built-in roles and existing users/assignments.
- Explicitly configured existing accounts gain SuperAdmin without resetting passwords,
  profile data, or existing roles. Repeated startups must be idempotent.
- Legacy Admin__ settings must not create or promote an account. Warn about their use;
  do not automatically promote the first registrant, all Admins, or an inferred user.
- The SuperAdmin owner can assign Admin to other accounts in Portal > Admin > Users.
  Preserve existing Admin/Teacher/Student authorization boundaries.
- Update CI to start with a SuperAdmin owner and the production release policy to require
  its runtime settings. Update the environment example and migration/setup instructions.
- Verify fresh-owner creation, existing-account credential preservation, legacy settings,
  incomplete settings, registration order, owner-only pages, and staff-role management.
- Before production promotion, add the encrypted SuperAdmin settings in both hosted apps,
  redeploy and verify the intended owner after a fresh sign-in. Remove obsolete Admin
  settings after owner access and the updated release workflow are verified.
- Production/staging configuration is not changed by this code PR. The current local
  DigitalOcean token cannot update the apps because it lacks database:read. Prior update
  requests were rejected; no runtime settings or production roles were changed.
- The ignored local .env was migrated to the owner keys with its existing values retained;
  no credentials belong in commits, logs, or the backlog.

Verification: Release build and formatting passed; all 693 unit/integration tests,
all 329 Full-mode browser tests, all 135 CI-selected Marketing browser tests, and
80 deployment-policy tests passed. Eight new startup cases cover fresh-owner creation,
idempotence, credential/profile/role preservation, ignored legacy Admin settings,
incomplete settings, and first-user safety. The new browser flow verifies audit-log
access and assigning Admin to another account in both modes. Existing ordinary Admin
permission tests pass. The existing watcher returned to Full mode with the original
local settings restored. No migration is required; no hosted settings were changed.

## B34 - P0 - Examination pagination and mobile question layout

**Estimated size: Small-Medium.** Pagination interaction fixes and mobile question-image ordering.
Status: completed and merged in [PR #70](https://github.com/chenggggggg/iD-Develops/pull/70), 2026-11-22. Production promotion/acceptance is separate.

- Mobile question layout, requested 2026-11-22: move the question image above the
  scenario/context so it no longer separates related scenario and question text.
  Intended mobile reading order: question image -> scenario/context -> question -> answers.
- Preserve the existing desktop media arrangement, question content, audio controls,
  saved answers, help text, responsive image sizing, and header/footer clearance.
- Apply the ordering consistently through the shared examination taking partial
  (`Pages/Shared/Examination/Questions/_QuestionBody.Take.cshtml`). Verify questions
  with image/scenario combinations and without images; avoid empty media gaps.
- Add browser checks for the mobile element positions and continued desktop layout,
  alongside the navigation gesture checks below. Include portrait/landscape coverage.
- Reproduce with a realistically long exam and actual input interactions; do not use
  keyboard focus auto-scrolling as the sole proof that the bar is user-scrollable.
- Verify mobile swiping, trackpad/horizontal wheel input, scrollbar access, keyboard
  navigation, active-question visibility, and Submit reachability in the deployed build.
- Desktop click-and-drag is not part of #67's native overflow implementation. Provide a
  reliable drag/scroll affordance where needed so off-screen question numbers are reachable.
- A drag must not accidentally choose another question; ordinary taps/clicks continue
  to navigate normally. Preserve vertical page scrolling and native touch behavior.
- Retain autosave, selected answers, question counts, timing, submission, focus, safe-area
  spacing, and the compact fixed action bar. Check desktop, portrait, and landscape.
- Add meaningful gesture/input regressions and document physical-device coverage limits.
  Do not duplicate the merged CSS overflow fix or claim it is deployed from merge alone.
Implemented: native touch scrolling remains available; desktop mouse dragging suppresses
accidental question selection, vertical wheel input scrolls question numbers while there
is room, horizontal trackpad input stays native, and focused navigation supports arrow,
Page Up/Down, Home, and End keys with localized instructions. Submit remains pinned.
Below the desktop breakpoint, images precede scenario/context, question, and answers;
desktop context-left/image-right columns are retained. No migration or settings needed.

Verification: Release build and formatting passed; all 685 unit/integration tests, 29
Full-mode Chromium exam checks, 28 WebKit exam checks, all 134 CI-selected Marketing
browser checks, and 75 deployment-policy tests passed. The new checks are included in
Marketing CI. Chromium native touch swipe/tap is verified through browser input (CDP);
that protocol-specific case is excluded from the WebKit selection, where the remaining
exam flows pass. Portrait/landscape screenshots were reviewed; no physical phone was
available. Original local settings were restored and the existing watcher is in Full mode.

## B35 - P1 - Portal product loading feedback and duplicate-create prevention

**Estimated size: Small-Medium.** Focused follow-up to completed B08; reuse its existing
button and Portal content-panel progress conventions.
Status: saved, 2026-11-22. Deferred at the user's request; do not implement with B34.

Reported on Portal > Products (`/products`): Create product and opening a selected
product give no visible loading feedback. Repeated clicks created eight products while
the user believed the initial action had not started. Treat this as a reported workflow
issue; investigate the handlers and confirm its root cause before implementation.

- Create product: immediately show a spinner/busy state on the responsible button and
  prevent further clicks from creating additional requests while creation is pending.
- Opening an existing product: show the established Portal content-panel loading state,
  keeping the left navigation visible; prevent redundant pending navigation clicks.
- Restore usable controls after failure, give actionable error feedback, and allow an
  intentional retry. Preserve keyboard access, accessible busy/status announcements,
  successful navigation, and browser Back behavior.
- Investigate whether creation also needs server-side request deduplication; do not
  assume a visual spinner alone protects against duplicate records.
- Add delayed-response browser regressions: one creation after rapid repeated clicks,
  visible progress for product navigation, and recovery/retry following failure.
- First inspect why these controls do not use B08's existing loading implementation.
  Wire them into that system and reuse its spinners, busy states, and duplicate-submit
  handling. Do not create a new loading feature or parallel implementation unless a
  demonstrated gap requires a minimal extension of the existing system. No global popup.

This is supporting Portal work, not a blocker for the public marketing launch.

## B36 - A2 exam import and multiple open-answer inputs

Status: completed and merged in [PR #73](https://github.com/chenggggggg/iD-Develops/pull/73), 2026-11-23.
Estimated size: Medium-Large; exam schema, editor, taking flow, scoring, snapshots,
import/export, results, and browser regressions form one coherent change.

- Add labelled inputs with stable keys, accepted alternatives, and optional partial-credit
  awards. Preserve old single-answer attempts; configured multi-input questions require
  every input to match. Keep partial/empty responses saveable and show incomplete inputs
  in the existing submit warning. Reuse existing autosave, navigation, and status feedback.
- Use Tailwind for editor cards/controls and desktop/mobile answer fields. Keep keyboard
  focus after additions/removals/reordering and wait for the autosave shell to be ready.
- Use one Maximum points control above the answers for every question type. Label open
  alternative awards explicitly, show their allowed range/full-credit value, and keep
  invalid points unsaved with inline feedback plus the existing save-failed status.
  Share maximum resolution across scoring, totals, grade-band settings, and results;
  bound legacy fallback awards to that maximum. Preserve legacy partial awards on edit.
- Keep mobile editor question navigation in its own scrollable row, separate from the
  Publish controls, with the add-question menu available outside the scrolling numbers.
- Persist definitions in additive JSONB columns and keep incremental snapshot changes
  scoped to changed keys/content. Hide all accepted answers from exam-taking views.
- Export format 3 with backward-compatible format-2 imports. Preserve readable grouped
  answers in public/Portal results and snapshot review. No new external service/settings.
- Prepare Test A2 from the two supplied Word documents: 60 questions (43 multiple-choice,
  17 open), 60 maximum points, 40 minutes as confirmed by the user. Question 60 has two
  inputs (`om`, `te`) for one joint point; question 1 includes the source's half-credit
  alternatives. Retain question 48 answer B and remove its feedback per the user's decision.
- Deliver a separate A2 import JSON and a source/formatting review list in the supplied
  A2 folder; preserve its existing reference JSON and both Word files. Media remains unset.

Verification: Release build and full formatting checks, all 729 unit/integration tests,
75 related Full-mode exam browser checks (including the normal-student Portal flow),
all 141 selected Marketing browser checks, and 80 deployment-policy tests passed.
JavaScript syntax, migration/model parity,
and full A2 import/publication/export/scoring in an isolated in-memory database passed.
All 59 retained feedback entries were checked against the source wording and arrows;
question 48 is intentionally empty. Desktop/mobile screenshots were reviewed; no physical
phone was available. Exact original local settings, consent, and Full mode were restored.
No production data, real emails, or hosted settings were changed. The supplied Word files
and original reference JSON remain untouched; the finalized files are test-a2.exam.json
and test-a2-review.txt in the supplied A2 folder. Use format 3 after this PR is deployed.

## B37 - P0 - Shared A1/A2 level-test selection page for marketing launch

**Estimated size: Medium-Large.** The current public flow supports one designated
`level-test` exam; this requires a complete two-test journey as well as the page redesign.
Status: implemented and verified on `feature/a1-a2-level-test-selection`, 2026-11-24.
Implementation and the dedicated branch were explicitly approved. Top priority for the
marketing launch, before B35 and other supporting/post-launch work. Commit, push, and
PR creation were explicitly approved, including the supplied Dominique homepage portrait;
no hosted configuration or deployment has been changed.

Requested journey: Level test CTA -> general level-test page -> choose/start A1 or A2.

- Keep one localized general landing page at `/level-test`, reached from existing public
  level-test CTAs. Explain the purpose and common process once, rather than repeating
  full introductions, benefits, instructions, or score guides for both tests.
- Redesign that page within the existing public identity, with clear, equally accessible
  A1 and A2 choices. Use concise level-specific guidance and the selected exam's actual
  duration/details to help visitors choose. Do not assume identical timing, scoring, or
  recommendations, and avoid a repetitive pair of long landing-page sections.
- Show any necessary test-specific introduction after selection, using the configured
  exam content where applicable. Keep the selection flow compact; browsing or choosing
  a level must not start a timed attempt until the visitor explicitly starts the test.
- Extend the existing designated-public-test configuration and server-side routing to
  support two independently configured/published tests. Current code hardcodes the single
  `level-test` slug in selection, completion, and results; do not solve this by only
  duplicating frontend buttons or exposing arbitrary private exams by ID.
- Carry the selected test and immutable attempt version through start, autosave, expiry,
  submission, completion, and emailed results. Reuse shared templates/components while
  retaining the correct exam introduction, title/level, maximum score, grade bands,
  feedback, and recommendations for A1 versus A2. Do not apply A1's score guide to A2.
- Preserve existing in-progress attempts, completed records, and protected emailed result
  links, including legacy `level-test` snapshots. Marketing visitors retain the existing
  public test/result workflow; this PR does not change account/Portal access policy.
- Handle one unavailable/unpublished test independently so the other remains usable;
  give clear availability feedback and never silently start the other level.
- Verify English/Dutch, desktop/mobile, keyboard/focus and navigation semantics, explicit
  start timing, both test journeys and their own result emails, unavailable/forged
  selections, and historical protected links in relevant Full/Marketing browser tests.
  Use the installed UI/UX skills when implementing and review the rendered design.

Implementation notes: reuse the existing public A1 `level-test` designation and add the
A2 `level-test-a2` slot; no schema migration or new environment variables are needed.
Admins choose the public level in Exam settings, one exam per slot, and publish each
exam. The generic page shows shared guidance once, then each selection shows its own
configured introduction and explicit Start action. Existing record/snapshot routes and
protected token purposes are preserved. Completion and email identify the exam; result
email links now preserve the English/Dutch route. Occupied-slot validation now renders
beside the public-test setting. No content/media is imported or replaced automatically.

Verification: Release build, full formatting, all 739 unit/integration tests, 7 new
Full-mode browser checks plus 75 related existing exam regressions (82 total), the exact
148 selected Marketing browser checks, 80 deployment-policy tests, and git diff --check
passed. Desktop/mobile Chromium screenshots were reviewed; physical-device verification
was not available. The original local settings, consent configuration, Full mode, and
public-test assignments were restored. Only dedicated local test data and File mail were
used; no production data, real email, or hosted setting was changed. The supplied Dominique
JPEG is converted to responsive 480/880-pixel WebP portraits (31/63 KB) and replaces only
her homepage portrait; Patrick and the About page retain their existing assets. All 26
homepage image/performance browser checks passed again in both Full and Marketing modes
after the portrait change, with desktop/mobile crops visually reviewed. Release build,
format verification, and all 739 unit/integration tests also passed after that addition.
Repeat the explicit A1/A2 assignments
and content checks in each environment: image promotion does not copy exam data.
Dependencies: B36's multiple-open-answer support (PR #73) for A2, and the correct A1/A2
exam content/media configured in the target environment. No implementation, branch,
commit/push, production configuration, or deployment is authorized by saving this item.

## B38 - P0 - Independent open-answer input scoring

Estimated size: Medium. Approved on 2026-11-24 on `fix/open-answer-group-scoring`,
including commit, push, and PR creation after verification. B37 is merged as PR #74
and deployed to Marketing production. This fix is merged as
[PR #75](https://github.com/chenggggggg/iD-Develops/pull/75). Its main CI run encountered
the intermittent no-script navigation failure tracked separately in B39.

- Derive each input maximum from its highest accepted-alternative award and sum input
  maxima for the question total. Sum matching input awards, including partial credit
  when another input is wrong or blank; never sum alternatives within one input.
- Make open-question Maximum points read-only, display input maxima, require explicit
  numeric awards in the editor, and independently calculate the persisted total server-side.
- Use the same calculation for grading, settings totals, snapshots, import/export, and
  public/Portal result review. Public results show earned/max points and partial status.
- Preserve original snapshot keys and answers. Resolve legacy blank awards by dividing
  the original question total between inputs, then materialize explicit awards on edit
  or transfer. Export format 4; accept formats 2/3 for existing files. No migration required.
- Regression-check the reported two 0.5-point om/te inputs, independent partial credit,
  alternatives, invalid awards, stale posted maxima, old snapshots, transfer compatibility,
  and responsive editor/result flows in Full and Marketing modes.

Verification passed: Release build, full formatting, all 750 unit/integration tests,
all 348 Full-mode CI-selected browser tests, all 153 Marketing CI-selected browser
checks, and 80 deployment-policy tests. The focused 19 media-warning scenarios also
passed. Media-persistence tests now confirm the calculated maximum is read-only and
save an editable input label, checking both database persistence and unchanged media
warnings/retry controls; they no longer attempt to edit the derived maximum.
Desktop/mobile screenshots were reviewed; no physical phone was available. Original
local settings, Full mode, and public-test assignments were restored. No production
records, exam content, or real email were changed. Staging deploys through the normal
main-merge workflow.

## B39 - P0 - Stabilize no-JavaScript level-test navigation checks

Estimated size: Small. Dedicated branch `fix/no-script-level-test-ci` approved on
2026-11-24 after PR #75 merged; implementation and verification are complete. The user
explicitly approved committing, pushing, and opening this follow-up PR after verification.

- The failing no-script selection check used a second, untraced browser context and
  uncontrolled font/icon CDN stylesheets. Its CI trace contained no page requests.
  Holding the font stylesheet reproduced the click-stability timeout on the actual
  local level-test page; releasing that stylesheet allowed native navigation.
- Move no-script checks into a managed context with JavaScript disabled from creation,
  using the existing diagnostic base and the same CDN isolation as other native-link
  checks. Keep application CSS and native mouse/keyboard navigation real; wait for
  stylesheet loading, without forced clicks, sleeps, retries, or longer test timeouts.
- Cover A1 and A2 in English/Dutch on mobile/desktop, checking the exact selected
  heading, introduction, Start action, touch target, destination, and absence of
  attempt/snapshot creation while browsing. Preserve regular visual and timed-test flows.
- Include the new no-script class in Marketing CI. Replace one case with four broader
  cases, for 351 Full-mode and 156 Marketing checks. Reuse the existing serialized
  public-test fixture and restore local public assignments after every case.

No application code, exam content, migration, production setting, or real email is
changed by this follow-up. Three focused no-script runs passed. Release build, full
formatting, all 750 unit/integration tests, all 351 Full-mode and 156 Marketing CI-selected
browser checks, and 80 deployment-policy tests passed. The managed trace now captures
the actual page/network rather than an unused context. Original local settings, Full
mode, consent configuration, and public-test assignments were restored. Publishing
proceeds under the user's explicit approval; staging follows the normal main merge.

## B40 - P1 - Refresh signup emails and show readable question labels

Estimated size: Medium. Implementation, branches, publishing, and merging B40-B42
were explicitly authorized on 2026-11-25, one PR at a time after successful checks.
B40 is merged as PR #77 and deployed to staging. Build, full formatting, 753 unit/
integration tests, 11 focused signup browser checks, and the full GitHub Full/Marketing
browser suites passed. Older queued email payloads remain deliverable without migration.

- Refresh the signup customer confirmation and company notification with the existing
  brand identity, clear product/contact/answer sections, readable spacing, and a layout
  that works in desktop and mobile email clients. Verify the logo asset and absolute URL.
  Use email-compatible markup and styling rather than relying on application Tailwind
  stylesheets or external web fonts being available in recipients' email clients.
- Display the configured question labels in form order and the applicable submission
  language, rather than internal keys such as `additional_remarks`. Snapshot labels,
  ordering, and language when queuing notifications so later form edits do not change
  the meaning of previously submitted answers. Support older queued payloads safely.
- Format multiple selections and multiline answers as readable lists or line breaks.
  Omit empty optional answer rows, and show personal details and quantity/participants
  once without leaking internal submission metadata into the custom-answer table.
- Preserve custom product subjects/introduction content, required company/customer
  notifications, safe answer encoding, submission deduplication, and durable delivery
  retries. Do not change signup workflow or create duplicate submission storage.
- Verify rendered emails with short/long labels, English/Dutch text, special characters,
  multiline/multiple-choice answers, blank optional fields, and legacy queued models;
  add relevant rendering/notification regressions and inspect desktop/mobile layouts.

Source-confirmed root cause: `wwwroot/templates/ProductEmailTemplate.cshtml` prints
`SubmittedValues` dictionary keys and sorts them alphabetically. The queued email model
currently contains the submitted key/value pairs without configured field-label metadata.
The form definitions already have labels, translations, and a sort order.

Signup registry status: `ProductFormSubmissions` already stores the form/product name,
submission timestamp, email, deduplication key, and JSON containing submitted personal
details and custom answers. Signup submission and notification queue rows are saved in
one transaction by `ProductFormSubmissionService.SaveWithNotificationsAsync`. There was
no Portal submission-list/detail interface at the time of the B40 inspection. B44 adds
that separate staff registry using the existing submission and notification storage.
This inspection confirms the code path, not the presence of any particular production record.

## B41 - P1 - Use stepped forms for every product with a form

Estimated size: Medium-Large. High-priority implementation and Git workflow authorized
on 2026-11-25. Implemented on `feature/all-product-form-steps` after B40 merged.
Release build, full-solution formatting, all 761 unit/integration tests, 80 deployment-
policy checks, and a complete 373-test Full browser run passed. Final affected-flow
verification passed 74 Full and 59 Marketing browser checks, including all nine staff
editor/previews, form-gated download entrypoints, protected staff file inspection,
grouped-answer validation, contact/result emails, and Marketing's existing payment/
account restrictions. Local file-mail inspection ignores only unrelated in-flight files
and serializes test cleanup; exact delivery assertions remain. Desktop/mobile screenshots
were reviewed. Local settings were restored. PR #78 passed final GitHub CI and is merged;
no migration is required.

- Apply the existing Signup product flow to every product with a configured form,
  regardless of product type: product details -> personal details -> product questions
  -> the appropriate checkout, booking, submission, or confirmation outcome.
- Always include the personal-details step for products with a form. Require name and
  a valid contact email; retain the existing optional phone/address field requirements
  rather than making every personal-detail field mandatory. Avoid asking for the same
  details again as custom questions.
- Reuse and generalize the existing step navigation, validation, accessible labels,
  progress feedback, and Back behavior instead of creating another form implementation.
  Preserve entered values when navigating steps or correcting validation errors.
- Preserve each product's configured payment/booking behavior and existing submission
  persistence, notifications, and duplicate-submission protection. Do not show a success
  confirmation before the relevant action succeeds. Products without a form retain
  their current direct action; Preview Only products follow B42 instead.
- Verify relevant product types in Full and Marketing modes, including required personal
  details, custom validation, back navigation, desktop/mobile, and payment/booking handoff.
  Coordinate with B40 so generalized submissions use readable email labels consistently.

## B42 - P1 - Show a Contact Us action for Preview Only products

Estimated size: Small-Medium. High-priority implementation and Git workflow authorized
on 2026-11-25. Implemented on `fix/preview-products-contact` after B41 merged.
Preview Only product pages and download cards now show localized Contact us links;
staff previews link to the public host. Direct transaction/download guards remain.
Release build, full formatting, 761 unit/integration tests, 80 deployment-policy checks,
and 54 Full / 50 Marketing browser checks passed. No migration or configuration change.
PR #79 passed all GitHub checks and is merged.

- Keep a visible, enabled primary action in the usual payment/signup button position
  for Preview Only products, but send it to the public Contact Us page rather than
  starting checkout, signup, booking, or the product form flow.
- Use clear localized Contact Us wording so the action describes its destination rather
  than promising payment. Implement navigation as an anchor styled with the established
  product button treatment, preserving the visitor's selected language.
- Keep existing server-side Preview Only guards against direct checkout/signup attempts;
  this changes the public enquiry route, not whether the product can be purchased.
  Preserve normal actions when Preview Only is off and staff editor preview behavior.
- Verify public product actions for Signup, paid, and booking/link-based products,
  Preview Only on/off, English/Dutch, desktop/mobile, and Full/Marketing modes. Ensure
  the contact action creates no purchase, signup, booking, or submission records.

## B43 - P1 - Link Free downloads from Pricing and the public footer

Estimated size: Small. Saved and explicitly authorized for implementation and the
same verified commit/push/PR/merge workflow on 2026-11-25, after B40-B42.
Implemented on `feature/free-downloads-navigation`: localized links at the bottom of
Pricing and in the public footer, with NT2 first and Pricing last. Release build, full
formatting, 761 unit/integration tests, 80 deployment-policy checks, and 26 Full / 25
Marketing browser checks passed. English mobile and Dutch desktop screenshots reviewed.
Local settings restored. PR #80 passed GitHub CI and is merged.

- Add a clear reference link to the existing Free downloads page at the bottom of
  Pricing and in the shared public footer; reuse the existing destination and localized
  navigation conventions rather than introducing another download page.
- Reorder the footer service links so NT2 & Integration is first and Pricing is last.
  Place Free downloads before Pricing while retaining the other service links.
- Preserve the selected English/Dutch language and verify the links and ordering on
  desktop/mobile in Full and Marketing modes. Do not change download/product behavior.

## B44 - P1 - Portal signup registry grouped by product and form

Estimated size: Medium. Implementation, branch creation, commit/push, and merge after
successful checks explicitly approved on 2026-11-25. Implemented on
`feature/portal-signup-registry`. Release build, full formatting, all 771 unit/integration
tests, 80 deployment-policy checks, migration/model consistency, and 69 Full / 67
Marketing browser checks passed. Desktop/mobile screenshots reviewed; local settings
restored. Implementation is complete; publish and merge only after successful GitHub CI.
Includes an additive metadata/index migration and a safe backfill of stored names/slugs.
Historical product IDs are not inferred from names or slugs. New submissions capture
product identity and question labels; original answer JSON and existing mail delivery
remain intact. No new environment variables are required.

- Add an Admin/SuperAdmin Portal signup list and submission detail screen, reusing
  existing ProductFormSubmissions and notification records, including older signups.
- Make product/form organization central to the UI: offer a product/form selector with
  signup counts and a clearly labelled list for the selected product/form. Provide an
  All signups view with a visible, sortable product/form column rather than forcing
  staff to read an undifferentiated list of unrelated forms.
- Support filtering and sorting by product/form, searching by customer name/email,
  date filtering, and pagination. Within a selected product/form, default to newest
  submissions first and allow date/name sorting. Keep filters and sorting when paging
  or returning from a submission detail screen.
- Keep the list concise: submission date, customer name/email, product/form, and
  notification status. Show full personal details and readable, ordered form answers
  on the detail screen; do not expose internal field keys as question labels.
- Preserve historical submission data and identify limitations in legacy product/form
  associations before choosing grouping keys. Do not lose older records after renames
  or combine unrelated products solely because their display names match.
- Reuse existing Portal tables, filters, feedback, and responsive design conventions.
  Verify role restrictions, product/form filtering and sorting, historical records,
  pagination, detail navigation, and desktop/mobile readability.

## External references checked

- Zoom server-to-server OAuth: https://developers.zoom.us/docs/internal-apps/s2s-oauth/
- Zoom concurrent meetings: https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0068522
- Zoom alternative hosts: https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0067027
- Cloudflare R2 public/custom-domain access: https://developers.cloudflare.com/r2/buckets/public-buckets/
- Editorial layout reference: https://www.shopify.com/blog
