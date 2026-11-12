# Planned PR backlog

Updated: 2026-11-10

This is the combined planning backlog from the staging review and owner meeting.
B01-B27 are planning IDs, not GitHub PR numbers. Current launch priorities are:
P0 marketing-release blockers, P1 supporting work, P2 post-launch improvements.
This file does not authorize implementation, Git changes, or deployments.

Workflow: before each PR, summarize its ID/title, priority, estimated size, and planned
changes for the user. B01 was explicitly started on 2026-10-31 on fix/payment-fulfillment-audit.

## Marketing launch scope - owner priorities, 2026-11-10

The first production release presents the company and its services, provides contact,
and supports the full public level-test journey through emailed results. The existing
portal remains available through its direct URL for staff operations. Removing public
navigation links does not change authentication or role permissions.

Launch order: B10 -> B22 -> B23 -> B24 -> B25 -> B26 -> B27.
B15 asset work can run alongside these; broken visible assets are launch blockers,
while exhaustive unused-image cleanup is supporting work. B10 already covers the
completion wallpaper. Its current layout shows the image on desktop and prioritizes
the form on mobile. The brighter homepage hero still awaits the user-supplied image.

Public result viewing remains independent of portal login. Retain the protected emailed
link; a record ID alone must not grant access. Signed-in attempts keep their account
association, while anonymous attempts can remain standalone. Automatic account creation,
staff-only portal restrictions, link expiry/reissue, and a shorter result URL are separate
decisions. Preserve current access boundaries while preparing the launch.

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
| B23 | Small | Shared desktop/mobile public navigation cleanup and regression checks while preserving direct portal access. |
| B24 | Medium | Establish and apply existing public typography, buttons, spacing, forms, and responsive component conventions for the launch pages. |
| B25 | Large overall | Redesign four service pages using approved content and shared patterns; deliver as independently reviewable page batches. |
| B26 | Medium | About-page layout, company information, existing imagery, contact paths, and localized responsive checks. |
| B27 | Medium | Production configuration and data separation, stable result-link keys, production URLs/email, and release smoke checks. |

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

New homepage media PR; homepage hero replacement awaits the image supplied by the user.

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

Dependencies: user-supplied hero; align reusable media handling with B16/B17. B10 handles
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
- B15 awaits the owner's supplied replacement hero image.

## Suggested implementation order

The 2026-11-10 marketing launch order supersedes the earlier feature-completion sequence:
B10 -> B22 -> B23 -> B24 -> B25 -> B26 -> B27. Run useful B15 asset work alongside these.
Existing completed functionality stays available to staff through the portal.
After launch, resolve B03's Zoom decision, continue the broader B12 identity work and
B13/B14 polish, and deliver blog work in the B16 -> B17 -> B18 sequence. B19 remains a
targeted performance investigation. B20 remains the final broader UI/UX audit.

## B22 - P0 - Exam-taking layout and header clearance

Status: implemented and verified on `fix/exam-taking-layout`; ready for PR review.
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

**Estimated size: Medium.** Focused taking-surface layout and responsive/browser regressions.

- Give scenario/context, question text, and answers one consistent left edge.
- Lay out context and optional media first, with the question immediately above its answers.
- Left-align multiple-choice labels and text. Slightly narrow the answer card while keeping
  long content, images, open answers, and touch controls usable.
- Account for the fixed examination header at desktop/mobile sizes so it cannot cover content.
- Preserve answer autosaving, question navigation, keyboard selection, timing, and submission.
- Keep unrelated editor pagination/input polish in B13.

## B23 - P0 - Marketing navigation and public entry points

**Estimated size: Small.** Shared public navigation and visible-link regressions.

- Remove Login/Portal links from desktop and mobile public navigation across public pages.
- Keep direct portal/login URLs and current staff workflows working.
- Preserve current authorization and account settings. Hiding links is not a staff-only
  authorization policy; broader access restrictions need a separate explicit decision.
- Keep working contact, consultation, and level-test CTAs. Identify commerce or portal-
  dependent public links that need an owner decision before they are advertised at launch.

## B24 - P0 - Public marketing design foundations

**Estimated size: Medium.** Bring forward the public-only part of B12 for launch.

- Establish evidence-backed public conventions from the existing homepage/layout and tokens.
- Apply shared typography, button geometry, spacing, content widths, form/focus states,
  and responsive patterns needed by B25/B26.
- Preserve recognizable website branding and the examination palette/structure.
- Use Tailwind and existing reusable patterns; scope changes to actual launch needs.

## B25 - P0 - Public service-page design and information

**Estimated size: Large overall.** Split into independently reviewable page batches.

- Refresh /enterprise, /nt2-integration, /personal-development, and
  /language-and-communication using B24's patterns.
- Make the offer, intended audience, and next step clear with approved existing content.
- Preserve English/Dutch localization and flag information requiring owner confirmation.
- Check mobile/desktop layouts and all advertised destinations, including visible links
  to service subpages. Additional unfinished destinations need an explicit scope decision.
- Keep working contact, consultation, and level-test paths prominent.

## B26 - P0 - About page and company information

**Estimated size: Medium.** Focused public page and content review.

- Refresh /about using the public foundations and actual company information.
- Use existing approved imagery and clear contact paths; do not invent company claims.
- Verify localized copy, responsive layout, headings, links, and image presentation.

## B27 - P0 - Marketing production launch and acceptance checks

**Estimated size: Medium.** Environment configuration and complete public-flow verification.

- Prepare production domains/configuration and separate production data from staging/test data.
- Keep protected-result signing keys persistent across deployments and confirm emailed links
  use the production public host and HTTPS.
- Verify production mail delivery, contact submission, level-test start/autosave/countdown/
  submit, completion, emailed results, and direct authorized staff portal access.
- Check advertised public routes, mobile layouts, broken media/links, public metadata,
  and private-result cache/indexing behavior.
- Confirm deployment and rollback steps before launch. Production activation is a separate
  deployment action, not implied by saving this backlog or implementing an earlier PR.
- Full customer portal exposure, automatic account creation, result-link expiry/reissue,
  and a short /results alias remain separate decisions.

## External references checked

- Zoom server-to-server OAuth: https://developers.zoom.us/docs/internal-apps/s2s-oauth/
- Zoom concurrent meetings: https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0068522
- Zoom alternative hosts: https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0067027
- Cloudflare R2 public/custom-domain access: https://developers.cloudflare.com/r2/buckets/public-buckets/
- Editorial layout reference: https://www.shopify.com/blog
