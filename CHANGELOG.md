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
- Hosting: persistent data-protection keys, optional forwarded headers, configurable HTTPS redirect;
  cookie-or-bearer default authentication scheme; string enums in API JSON.
- DevOps: Dockerfile, docker-compose (app + PostgreSQL), GitHub Actions CI, `scripts/smoke.sh`.

### Fixed
- Login page no longer lists the internal cookie-or-bearer authentication scheme as an external login provider.
