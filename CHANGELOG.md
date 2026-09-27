# Changelog

All notable changes to **SmartSurvey** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
