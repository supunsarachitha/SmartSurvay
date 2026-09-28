# Changelog

All notable changes to **SmartSurvey** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Workspaces (multi-tenancy, in progress on `feature/multi-workspace`):** every survey, response, report and audit
  entry now belongs to a workspace, and the data layer only ever returns the current workspace's rows (fail-closed
  query filters plus a save guard that stamps and checks `WorkspaceId`). Share links stay `/s/{slug}`; people who are
  not members of a survey's workspace answer as guests, and surveys of disabled workspaces are unavailable.
  `GET /api/v1/public/surveys?workspace={slug}` lists a workspace's public surveys. Migration `AddWorkspaces` moves the
  data of an existing installation into "Default workspace" (its users become members, admins stay admins). New role
  `SuperAdmin` (seeded; the System console follows in later phases). Seeder settings `Seed:WorkspaceName/WorkspaceSlug`.
- **Workspace services:** workspace settings for its admins (name, description, contact, join link on/off, public
  survey page on/off); system settings, workspace administration (create with first admin, rename, enable, disable
  with a reason, approve, delete) and a system overview for super admins; self-service sign-up (creator becomes the
  admin; can be switched off or require approval) and joining a workspace with its link; separate system audit log.
  User administration is scoped: workspace admins manage only their own members, super admins every account.
- **Super admin bootstrap:** `Seed:SuperAdminEmail` / `Seed:SuperAdminPassword` (Docker: `SUPERADMIN_EMAIL`,
  `SUPERADMIN_PASSWORD`) create the first super admin. Demo data adds a second workspace "Acme Research"
  (`admin@acme.local`, admin password) to show the isolation.

### Changed
- Branding can only be changed by super admins (it is system-wide); `PUT/POST/DELETE /api/v1/branding*` require the
  SuperAdmin role.
- **User guide** at `/guide`: plain-language, step-by-step help with screenshots for respondents and administrators
  (linked from the top menu, footer, FAQ and admin sidebar).
- **GitHub links** in the top menu, admin sidebar, home page and footer (`Support:GitHubUrl`); the Buy Me a Coffee page
  points to <https://buymeacoffee.com/jkhy9gtjs>.
- **Docker instructions** in the README (install, start, everyday commands, backup/restore, settings, troubleshooting),
  a documented `.env.example`, and optional SMTP settings in `docker-compose.yml`.
- README screenshots gallery and badges.
- **Password-protected surveys:** an optional survey password (Settings → "Require a password to open the survey").
  Only a salted PBKDF2 hash is stored; respondents enter the password once and receive a signed, time-limited access
  key that the server checks when the survey is opened, a draft is saved and a response is submitted. Protected surveys
  are hidden from the public list, show their thank-you message in place, and are marked with a lock in the admin list.
  API: `POST /api/v1/public/surveys/{slug}/unlock`, header `X-Survey-Access-Key`, `accessKey` in submissions.
  Migration `AddSurveyAccessPassword`.
- **Bot and spam protection** for anonymous responses, without CAPTCHAs or third-party services: an ALTCHA-style
  proof-of-work challenge solved invisibly in a Web Worker while people answer, a minimum answering time, a honeypot
  field, one-time challenges and the existing rate limits (`BotProtection:*`, `BOT_PROTECTION` in Docker). API clients
  submitting anonymously include the solved `challenge`; signed-in callers are not challenged.
- **Encryption at rest:** respondents' written answers (text, paragraph and e-mail questions), "Other" texts and browser
  details are encrypted in the database with ASP.NET Core Data Protection (AES-256); the keys stay in the key ring
  (`DataProtection:KeysPath` / the Docker `keys` volume), not in the database. Existing values are encrypted once at
  start-up; values without a key show a placeholder instead of an error. `Encryption:Enabled` (`ENCRYPTION` in Docker).
  Migration `EncryptedAnswerColumns` widens those columns to `text`. Surfaced in the survey intro, response detail,
  export menu, guide, FAQ, README (including how to back up the keys) and documentation.

### Fixed
- Start-up encryption of older plain-text answers could loop forever when combined with workspace scoping; it now
  runs in system scope, stops when rows cannot be loaded, and orders its batches (no more EF Core 10102 warnings).
- `scripts/container-smoke.sh` accepts an empty admin password to skip the admin checks (avoids lockout counts on stacks
  with a changed admin password).
- The user guide no longer scrolls sideways on phones, and its screenshots scale to the text column and screen height
  (click to enlarge).

### Changed
- **License:** SmartSurvey is now source-available under the PolyForm Noncommercial License 1.0.0 (`LICENSE.md`);
  the repository owner keeps all rights, including commercial use. The footer's "Open source" heading became "Project"
  with a link to the license.

## [1.0.0] - 2026-09-27

First complete release: survey design with conditional logic, respondent runner, reports and exports,
administration, REST API, e-mail, Docker and CI.

### Added
- Project repository, development plan (`DEVELOPMENT_PLAN.md`) and changelog.
- .NET 8 SDK pinned via `global.json`; central package management (`Directory.Packages.props`);
  shared build settings (`Directory.Build.props`); local `dotnet-ef` 8.0.31 tool.
- Clean-architecture solution: `SmartSurvey.Domain`, `.Application`, `.Infrastructure`, `.Web`,
  `SmartSurvey.UnitTests`, `SmartSurvey.IntegrationTests`.
- Domain model: surveys, sections (pages), questions (10 types), options with "Other → free text",
  show/hide logic rules with All/Any conditions, responses/answers/selections, report definitions
  and widgets, audit log, Identity user with GUID keys.
- EF Core `AppDbContext` with PostgreSQL (jsonb settings, timestamptz) and SQLite provider switch,
  UTC and enum-as-string conventions, auditable-entity interceptor, `InitialCreate` migration.
- Application contracts (DTOs + service interfaces) for surveys, responses, reports, exports,
  dashboard, users and audit; conditional-logic evaluator and answer validator shared by UI and server.
- Web host: cookie + bearer (Identity API) authentication, role policies, API-friendly 401/403,
  ProblemDetails error handling, rate limiting, health checks, Swagger.
- Design system (Bootstrap 5.3.8, Bootstrap Icons, Inter; light/dark theme), public and admin layouts,
  shared UI components (page header, badges, modal, confirm dialog, toasts, pager, empty state, charts).
- 72 unit tests for logic evaluation, condition matching, answer validation, slugs and persistence.
- **SurveyService**: survey CRUD with graph reconciliation, normalisation (ids, order, question codes, slugs),
  comprehensive design validation (incl. logic ordering rules), lifecycle transitions, duplicate/templates,
  JSON import/export, optimistic concurrency, audit logging (213 tests).
- **ResponseService**: eligibility checks (schedule, quota, login, one response per user), start/resume with drafts,
  logic-aware server-side validation on submission, admin response browsing/detail/deletion (150 tests; also verified
  against PostgreSQL).
- **Reporting**: report engine (filters by date/status/answers, choice distributions with "Other" texts, rating/number
  statistics, cross-tabs, responses over time, text lists, raw grids), server-side SVG charts (bar, horizontal bar, pie,
  doughnut, line, stacked), `ReportService` (CRUD, duplicate, automatic default report, live preview, run, export) and
  exporters for PDF (QuestPDF, vector charts), CSV (UTF-8 BOM, formula-injection safe), TXT (ASCII tables), Excel and
  JSON; raw response export (CSV/Excel/JSON). 79 tests.
- **Administration services**: audit log (queryable, truncation-safe), dashboard statistics (30-day trend, top surveys,
  recent responses), user administration (create, roles, lock/unlock, delete; protects the last administrator and the
  acting admin), start-up seeding of roles, the initial admin and optional deterministic demo data. 28 tests.
- **Branding**: administrators can customise the product name, tagline and brand icon (built-in Bootstrap icon or an
  uploaded PNG/JPEG/GIF/WebP/ICO/SVG logo, also used as favicon). Cached `IBrandingService`, public
  `/branding/logo` and `/branding/favicon` endpoints, `BrandMark` component in all layouts, product name in page titles
  and export footers, `AddBrandingSettings` migration, `Branding` configuration section for initial values.
- **REST API** (`/api/v1`, cookie or bearer): surveys (CRUD, status, duplicate, templates, slug check, definition
  export/import, responses list and raw export), public respondent endpoints (available surveys, start/resume, submit,
  drafts, branding), my responses, response detail/delete, reports (CRUD, duplicate, default report, run, preview,
  export), dashboard, users, audit log and branding administration; OpenAPI descriptions for every endpoint.
  `docs/examples`: an .http walkthrough plus survey, response and report JSON examples. 35 integration tests.
- **Public pages**: landing page with branded hero and feature overview, searchable FAQ, Buy Me a Coffee support page,
  open-surveys page (start/continue/completed states, member-only hint for guests) and "My responses"; restyled sign-in,
  registration, password-reset and account-settings pages; the user menu shows the account's display name.
- **Survey management UI**: survey list with search, status/template filters, paging and actions (publish, close,
  reopen, archive, restore, duplicate, save as template, export/import definition, delete); survey builder with pages,
  10 question types, answer options (incl. "Other" with text box and pasting a list), per-type answer settings,
  question and page display logic, outline, inline server validation and unsaved-changes protection; share page with
  link, QR code (SVG/PNG) and invitation text.
- **Survey runner**: multi-page answering at `/s/{slug}` with live conditional logic, per-page and final validation
  (messages clear as answers are fixed), progress bar, question numbers that follow the respondent's path, shuffled
  options for "randomise" questions ("Other" stays last), automatic draft saving for signed-in respondents (page
  changes, leaving, "Save & finish later") with resume and start-over, a leave warning for guests and a thank-you page
  with the survey's message (and "answer again" where allowed). Administrators get a preview with desktop/phone width.
- **Reports UI**: report list (search, survey filter, one-click overview report, export, duplicate, delete), report
  builder (response filters incl. answer filters, widgets with type-aware options, recommended or blank start, live
  preview on current data, inline validation) and report viewer (refresh, print, duplicate, export as PDF, Excel, CSV,
  text or JSON).
- **Administration UI**: dashboard (KPIs, 30-day trend, top surveys, latest responses), per-survey response browser
  (status/date/respondent filters, raw export as CSV/Excel/JSON with optional drafts, delete), response detail (answers
  grouped by page, print, delete), user management (create, grant/revoke administrator, lock/unlock, delete), audit log
  (search, type and date filters, links to the affected items) and branding page (name, tagline, icon picker, logo
  upload, reset, live preview).
- **E-mail**: account confirmation and password-reset e-mails are sent through SMTP (MailKit; `Email:FromAddress`,
  `Email:FromName`, `Email:Smtp:*`) as branded HTML with a plain-text part. Without a configured server nothing is sent
  and the message is logged (full text only in Development). Administrators can set a new password for any user (Users
  page, `POST /api/v1/users/{id}/password`), e.g. when e-mail isn't configured.
- **Embedding**: surveys can be embedded in other websites via `/embed/s/{slug}` (snippet on the share page);
  `Embedding:Enabled` / `Embedding:AllowedOrigins` settings. All other pages refuse to be framed by other sites
  (`X-Frame-Options` + CSP `frame-ancestors`).
- Hosting: persistent data-protection keys, optional forwarded headers, configurable HTTPS redirect;
  cookie-or-bearer default authentication scheme; string enums in API JSON.
- DevOps: Dockerfile, docker-compose (app + PostgreSQL), GitHub Actions CI, `scripts/smoke.sh`.

### Security
- Every response sends `X-Content-Type-Options: nosniff` and `Referrer-Policy: strict-origin-when-cross-origin`;
  pages refuse to be framed by other sites (except `/embed`).
- Survey submissions from the interactive runner are limited per connection (5 per minute) — the REST API already
  had a per-IP limit.
- Test dependency upgrade to bUnit 2.11 (fixes the AngleSharp advisory GHSA-pgww-w46g-26qg); no known vulnerable
  packages remain.

### Fixed
- Expected API errors (validation, not found, conflicts, business rules) are no longer logged as server errors with
  stack traces.
- Development start-up no longer fails because the account e-mail sender was registered with the wrong lifetime.
- Failed sign-ins on the login page now count towards the account lockout (5 attempts → 15 minutes).
- Password forms require at least 8 characters, matching the Identity policy; self-registered accounts get the User role.
- Local time display falls back to UTC when the browser reports no time zone.
- Login page no longer lists the internal cookie-or-bearer authentication scheme as an external login provider.
