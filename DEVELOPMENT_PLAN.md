# SmartSurvey — Development Plan

> **This file is the single source of truth for project progress.**
> If a Claude Code session is renewed/reset, read this file first (especially
> [§ 9 Progress Log](#9-progress-log) and the phase checklists), then `git log --oneline`,
> and continue with the first unchecked task. Update this file after every completed task/phase.

---

## 1. Goal

A full-stack, modular, well-commented **survey application**:

* **Admins** build surveys with dynamic question types, answer options, "Other → free text"
  combined options and **conditional (show/hide) logic**; publish/close/archive them; browse
  responses; build **dynamic reports** (tables + charts) and **export** them as **PDF (QuestPDF)**,
  **CSV**, **TXT** (+ XLSX and JSON as extras).
* **Users** register/log in and answer surveys (multi-page, progress bar, save & resume).
* Extra pages: **FAQ**, **Buy Me a Coffee**.

## 2. Tech Stack (decided)

| Concern | Choice | Notes |
|---|---|---|
| Runtime | **.NET 8 / ASP.NET Core 8** (`net8.0`) | Pinned via `global.json` (SDK 8.0.4xx). TFM centralised in `Directory.Build.props` → upgrading to .NET 10 LTS is a one-line change. ⚠ .NET 8 support ends **2026-11-10**. |
| Frontend | **Blazor Web App, Interactive Server** render mode (per-page interactivity) | Static SSR for public/marketing pages + Identity pages; `@rendermode InteractiveServer` for builder, runner, reports, admin. |
| ORM | **EF Core 8.0.31** | Code-first, migrations in Infrastructure. `IDbContextFactory` pattern (Blazor Server safe). |
| Database | **PostgreSQL 16** (Npgsql EF 8.0.11) | Alternative provider **SQLite** (config switch) for demos/tests (uses `EnsureCreated`). |
| Auth | **ASP.NET Core Identity** (Guid keys) — cookie for UI, **Identity bearer tokens** for REST API (`MapIdentityApi`) | Roles: `Admin`, `User`. |
| API | **Minimal APIs** grouped under `/api/v1`, **Swagger** (Swashbuckle 10) | ProblemDetails error contract. |
| Validation | **FluentValidation 12** | Request DTO validation inside services. |
| PDF | **QuestPDF 2026.9.x** (Community license) | Charts embedded as SVG. |
| XLSX (extra) | **ClosedXML 0.105** | |
| QR codes (extra) | **QRCoder 1.8** | Share page. |
| E-mail | **MailKit 4.18** | SMTP delivery of account e-mails (`Email:*` settings). |
| Charts | Home-grown **server-side SVG chart renderer** | Same SVG used in the browser and inside PDFs → consistent, zero JS, fully unit-testable. |
| Tests | xUnit 2.9, bUnit 2.11, `WebApplicationFactory` + SQLite in-memory (DI scope validation on) | |
| DevOps | Dockerfile, docker-compose (app + postgres), GitHub Actions CI | |

Deliberately **not** used (licensing changes in 2025): AutoMapper (manual mapping), FluentAssertions (plain xUnit asserts), MediatR.

## 3. Architecture

Clean Architecture, 4 production projects + 2 test projects:

```
SmartSurvey.sln
├─ src/
│  ├─ SmartSurvey.Domain          Entities, enums, value objects (no EF / no ASP.NET deps;
│  │                              only Microsoft.Extensions.Identity.Stores for ApplicationUser)
│  ├─ SmartSurvey.Application     DTOs, service interfaces + implementations, FluentValidation
│  │                              validators, conditional-logic evaluator, response validator,
│  │                              report engine, SVG chart renderer, exporter abstractions.
│  │                              Depends on Domain + EF Core abstractions (IAppDbContext).
│  ├─ SmartSurvey.Infrastructure  AppDbContext (IdentityDbContext), entity configurations,
│  │                              migrations, JSON value converters, exporters (QuestPDF, CSV,
│  │                              TXT, XLSX, JSON), user admin (Identity), audit, seeding, DI.
│  └─ SmartSurvey.Web             Blazor Web App (UI) + Minimal API endpoints + Identity UI,
│                                 auth, Swagger, health checks, rate limiting, exception handling.
└─ tests/
   ├─ SmartSurvey.UnitTests         Logic evaluator, validators, services (SQLite in-memory),
   │                                report engine, exporters, SVG renderer, bUnit components.
   └─ SmartSurvey.IntegrationTests  WebApplicationFactory + SQLite: API routes end-to-end.
```

Key principles

* **Blazor components call Application services in-process**; the REST API is a thin layer over
  the *same* services (for mobile/3rd-party integrations). No duplicated business logic.
* **Conditional logic + answer validation run the same C# code** in the UI (live) and on the server
  at submission (never trust the client).
* Services obtain short-lived contexts via `IAppDbContextFactory` (safe for long-lived Blazor circuits).
* `ICurrentUser` (scoped) is populated by middleware for HTTP requests and by a `CircuitHandler`
  for Blazor circuits.
* Timestamps are **UTC `DateTime`** (portable across PostgreSQL/SQLite); numbers are `double`;
  enums stored as strings; settings objects stored as JSON (`jsonb` on PostgreSQL).
* Exporters are pluggable: implement `IReportExporter` and register it — the UI/API pick it up.

## 4. Data Model (contract)

```
ApplicationUser : IdentityUser<Guid>   DisplayName, CreatedAt, LastLoginAt, IsActive
Survey (auditable)                     Title, Description, Slug(unique), Status(Draft|Published|Closed|Archived),
                                       IsTemplate, AllowAnonymous, AllowMultipleResponses, ShowProgressBar,
                                       ShowQuestionNumbers, OpensAt?, ClosesAt?, MaxResponses?, WelcomeMessage,
                                       ThankYouMessage, PublishedAt?, ClosedAt?, Version(concurrency)
 ├─ SurveySection                      Title, Description, Order                       (pages)
 │   └─ Question                       Type, Text, Description, Code, Order, IsRequired, Settings(JSON)
 │       └─ QuestionOption             Text, Value, Order, AllowsFreeText ("Other → text"), FreeTextPlaceholder
 ├─ LogicRule                          TargetQuestionId? | TargetSectionId?, Action(Show|Hide), MatchType(All|Any)
 │   └─ LogicCondition                 SourceQuestionId, Operator, OptionId?, Value?
 ├─ SurveyResponse                     RespondentId?(null=anonymous), Status(InProgress|Completed), StartedAt,
 │   │                                 UpdatedAt?, SubmittedAt?, CurrentSectionIndex, UserAgent
 │   └─ Answer                         QuestionId, TextValue?, NumberValue?(double), DateValue?(DateOnly)
 │       └─ AnswerSelection            OptionId, FreeText?
 └─ ReportDefinition (auditable)       Name, Description, Filters(JSON: date range, includeInProgress, answer filters)
     └─ ReportWidget                   Title, Order, Type, QuestionId?, SecondaryQuestionId?, Settings(JSON)
AuditLogEntry                          Timestamp, UserId?, UserName?, Action, EntityType, EntityId?, Details?
```

Question types: `ShortText, LongText, Radio, Checkbox, Dropdown, Number, Email, Date, Rating, Scale`
(combined types = any choice option with `AllowsFreeText = true`, e.g. "Other (please specify)").

Condition operators: `Equals, NotEquals, Contains, NotContains, GreaterThan, GreaterThanOrEqual,
LessThan, LessThanOrEqual, IsAnswered, IsNotAnswered`.

Report widget types: `SummaryStats, QuestionTable, BarChart, HorizontalBarChart, PieChart,
DoughnutChart, LineChart (responses over time), CrossTab, TextResponses, RawResponses`.

## 5. Feature List

### Required
- [x] Survey CRUD with dynamic question types (text, radio, checkbox, dropdown) + options
- [x] Combined types ("Other" → free-text field) via `AllowsFreeText` options
- [x] Conditional logic (show/hide questions & sections; All/Any conditions)
- [x] User login + answering surveys
- [x] Dynamic report builder (tables + charts) over existing data
- [x] Export PDF (QuestPDF), CSV, TXT
- [x] Architecture, DB schema, EF Core migrations, API routes, Blazor components, examples
- [x] FAQ page, Buy Me a Coffee page
- [x] Git repo, documentation file, changelog, this plan

### Added (commonly found in SurveyMonkey / Google Forms / Typeform-class apps)
- [x] Extra question types: long text, number, email, date, star rating, linear scale / NPS
- [x] Per-question settings: placeholder, min/max length, min/max value, min/max selections, randomise options
- [x] Multi-page surveys (sections) with progress bar, per-page validation
- [x] Save & resume drafts (logged-in users) with auto-save on page change
- [x] Survey lifecycle: Draft → Published → Closed → Archived; schedule (opens/closes at); response quota
- [x] Anonymous/public surveys via share link; one-response-per-user option
- [x] Share page with link + QR code + embed snippet
- [x] Preview mode for admins
- [x] Duplicate survey, templates ("create from template"), import/export survey definition (JSON)
- [x] Response browser: list, filter, view individual response, delete; raw export (CSV/XLSX/JSON)
- [x] Report filters (date range, include in-progress, answer-based filters), cross-tabulation,
      numeric statistics (mean/median/min/max/std-dev), responses-over-time chart,
      auto-generated "default report" for any survey, duplicate report
- [x] Extra export formats: XLSX, JSON; CSV-injection protection
- [x] **Branding (user request 2026-09-27):** admins customise product name, tagline and icon (built-in Bootstrap icon or uploaded logo, also used as favicon) at `/admin/branding`; used by navbar, sidebar, footer, page titles, favicon and export footers
- [x] Admin dashboard (KPIs, responses per day, top surveys)
- [x] User management (roles, lock/unlock, create user)
- [x] Audit log
- [x] REST API with bearer tokens, Swagger, ProblemDetails, rate limiting, health checks
- [x] Dark mode toggle
- [x] Seed data: admin + demo user, example surveys with logic, sample responses, sample report
- [x] Docker / docker-compose, CI workflow

(Checked items = in scope; see phase checklists for implementation status.)

## 6. Routes

### UI (Blazor)
| Route | Access | Purpose |
|---|---|---|
| `/` | public | Landing page / features |
| `/faq` | public | FAQ |
| `/buy-me-a-coffee` | public | Support page (username configurable: `Support:BuyMeACoffeeUsername`) |
| `/surveys` | public/users | Available surveys |
| `/s/{slug}` | per survey | Take survey (multi-page runner) |
| `/s/{slug}/thank-you` | per survey | Completion page |
| `/embed/s/{slug}`, `/embed/s/{slug}/thank-you` | per survey | Runner / completion page for iframes on other sites (minimal layout; see `Embedding:*`) |
| `/my/responses` | user | My submissions & drafts |
| `/Account/*` | public | Identity (login, register, manage, 2FA …) |
| `/admin` | admin | Dashboard |
| `/admin/surveys` | admin | Survey list, create, templates, import |
| `/admin/surveys/{id}/edit` | admin | Survey builder (questions, logic, settings) |
| `/admin/surveys/{id}/preview` | admin | Preview |
| `/admin/surveys/{id}/share` | admin | Link, QR code, embed |
| `/admin/surveys/{id}/responses` | admin | Responses list + raw export |
| `/admin/responses/{id}` | admin | Response detail |
| `/admin/reports` | admin | Report list |
| `/admin/reports/new`, `/admin/reports/{id}/edit` | admin | Report builder with live preview |
| `/admin/reports/{id}` | admin | Report viewer + export buttons |
| `/admin/users` | admin | User management |
| `/admin/audit` | admin | Audit log |
| `/admin/branding` | admin | Product name, tagline, icon / logo |

### REST API (`/api/v1`, cookie or bearer auth)
| Method & Route | Access | Purpose |
|---|---|---|
| `POST /api/auth/login`, `/register`, `/refresh` … | public | Identity API endpoints (bearer tokens) |
| `GET /api/v1/surveys` | admin | List (search, status, paging) |
| `GET /api/v1/surveys/{id}` | admin | Full definition |
| `POST /api/v1/surveys` | admin | Create |
| `PUT /api/v1/surveys/{id}` | admin | Update (optimistic concurrency via `version`) |
| `DELETE /api/v1/surveys/{id}` | admin | Delete |
| `POST /api/v1/surveys/{id}/status` | admin | Publish / close / archive / reopen |
| `POST /api/v1/surveys/{id}/duplicate` | admin | Duplicate / create from template |
| `GET /api/v1/surveys/templates` | admin | Templates |
| `GET /api/v1/surveys/{id}/definition` | admin | Export definition JSON |
| `POST /api/v1/surveys/import` | admin | Import definition JSON |
| `GET /api/v1/surveys/{id}/responses` | admin | Paged responses |
| `GET /api/v1/surveys/{id}/responses/export?format=csv\|xlsx\|json` | admin | Raw export |
| `GET /api/v1/responses/{id}` | admin/owner | Response detail |
| `DELETE /api/v1/responses/{id}` | admin | Delete response |
| `GET /api/v1/public/surveys` | public/user | Available surveys |
| `GET /api/v1/public/surveys/{slug}` | per survey | Survey session (definition + draft + eligibility) |
| `POST /api/v1/public/surveys/{surveyId}/responses` | per survey | Submit response |
| `PUT /api/v1/public/surveys/{surveyId}/draft` | user | Save draft |
| `GET /api/v1/me/responses` | user | My responses |
| `GET/POST /api/v1/reports`, `GET/PUT/DELETE /api/v1/reports/{id}` | admin | Report CRUD |
| `POST /api/v1/reports/{id}/duplicate` | admin | Duplicate |
| `POST /api/v1/reports/default/{surveyId}` | admin | Auto-generate report |
| `GET /api/v1/reports/{id}/run` | admin | Execute → JSON result |
| `POST /api/v1/reports/preview` | admin | Execute unsaved definition |
| `GET /api/v1/reports/{id}/export?format=pdf\|csv\|txt\|xlsx\|json` | admin | Export file |
| `GET /api/v1/dashboard` | admin | Dashboard KPIs |
| `GET /api/v1/users`, `POST /api/v1/users`, `PUT /api/v1/users/{id}/roles`, `POST /api/v1/users/{id}/password`, `POST /api/v1/users/{id}/lock\|unlock` | admin | User admin |
| `GET /api/v1/audit` | admin | Audit log |
| `GET /api/v1/public/branding`, `PUT /api/v1/branding`, `POST/DELETE /api/v1/branding/logo`, `POST /api/v1/branding/reset` | public / admin | Branding |
| `GET /branding/logo`, `GET /branding/favicon` | public | Brand images (versioned caching) |
| `GET /health` | public | Health check |
| `/swagger` | dev/configurable | OpenAPI UI |

## 7. Phases & Tasks (execute sequentially)

Legend: `[ ]` todo · `[~]` in progress · `[x]` done

### Phase 0 — Environment & repository
- [x] Install .NET 8 SDK (8.0.425) — winget `Microsoft.DotNet.SDK.8`
- [x] Verify Docker Desktop, Node, git
- [x] `git init`, `.gitignore`, `global.json`
- [x] `DEVELOPMENT_PLAN.md` (this file), `CHANGELOG.md`
- [x] PostgreSQL 16 for development (native Windows service via winget; docker-compose provided for containers)

### Phase 1 — Solution scaffolding
- [x] Solution + 4 src projects + 2 test projects, project references
- [x] `Directory.Build.props` (TFM, nullable, warnings), `Directory.Packages.props` (central package versions)
- [x] Local tool manifest with `dotnet-ef` 8.0.31
- [x] Blazor template (Individual auth, Interactive Server) cleaned: move ApplicationUser → Domain, DbContext → Infrastructure
- [x] Solution builds

### Phase 2 — Domain & persistence
- [x] Domain entities, enums, value objects (QuestionSettings, WidgetSettings, ReportFilterSet)
- [x] AppDbContext + entity configurations (indexes, cascades, JSON converters, UTC converter)
- [x] Provider switch (PostgreSQL | Sqlite), auditable-entity interceptor
- [x] Initial EF Core migration (PostgreSQL)

### Phase 3 — Application contracts (the "spine")
- [x] Common: exceptions, PagedResult, ICurrentUser, IAppDbContext(+Factory), AppRoles
- [x] DTOs + service interfaces: Surveys, Responses, Reports, Dashboard, Users, Audit, Exports
- [x] LogicEvaluator (show/hide, chained) + ResponseValidator (pure, shared by UI & server)
- [x] Report result model + SVG chart renderer contract + exporter contract
- [x] DI registration skeleton; Program.cs skeleton; shared UI components; nav

### Phase 4 — Backend services
- [x] 4A SurveyService: CRUD + graph reconciliation, slug, status transitions, duplicate/templates, import/export, validators
- [x] 4B ResponseService: eligibility, start/resume, drafts, submit (server-side logic + validation), admin listing/detail/delete
- [x] 4C Reporting: ReportService CRUD, ReportEngine (filters, aggregations, crosstab, stats, time series), SVG charts, exporters (PDF/CSV/TXT/XLSX/JSON), raw response export
- [x] 4D Infrastructure services: AuditService, UserAdminService, DashboardService, DbSeeder (demo data)
- [x] Unit tests for each track (564 unit tests green; separate agent review rounds dropped — see "Working mode")

### Phase 5 — Web host & REST API
- [x] Program.cs: Identity (cookie + bearer), policies, API 401/403 handling, rate limiting, health, Swagger, exception handler
- [x] Minimal API endpoint groups (surveys, public, responses, reports, dashboard, users, audit, branding)
- [x] `docs/examples/*.http` + JSON examples (verified by `DocumentedExampleTests`)
- [x] Integration tests (WebApplicationFactory + SQLite): 35 tests

### Phase 6 — Blazor UI
- [x] 6A Layout, nav, dark mode, shared components, Home, FAQ, Buy Me a Coffee, surveys list, my responses, account pages restyle
- [x] 6B Admin: survey list/create/import/templates, builder (sections, questions, options, settings, logic), share page
- [x] 6C Respondent: survey runner (multi-page, logic, validation, drafts), preview, thank-you (my responses done in 6A). `RandomizeOptions` honoured (per-respondent shuffle, free-text options last). Embedding decided: `/embed/s/{slug}` pages only, frame headers set by `SecurityHeadersMiddleware` (antiforgery's X-Frame-Options suppressed), `Embedding:*` config, snippet on the share page
- [x] 6D Admin reports: report list (search, survey filter, quick overview, export/duplicate/delete), builder (details, response filters incl. answer filters, widget editor with type-aware settings, recommended/blank start, debounced live preview on a snapshot, server errors mapped to fields/widgets/filters), viewer (refresh, print, duplicate, export PDF/Excel/CSV/TXT/JSON)
- [x] 6E Admin: dashboard (KPIs, 30-day chart, top surveys, latest responses), responses browser (filters, raw export CSV/Excel/JSON incl. drafts, delete) and detail (answers by page, print, delete), users (create with inline validation, grant/revoke admin, lock/unlock, delete; self-protection), audit log (filters, readable actions, links), **branding page** (name, tagline, icon picker, logo upload/remove, reset, live preview, layout refresh after saving)

### Phase 7 — Verification
- [x] SMTP e-mail sender (`Email:*` settings, MailKit 4.18) replacing the template's no-op sender: branded HTML + text account e-mails; without a host messages are only logged (bodies only in Development); plus admin "Set new password" (service, API `POST /api/v1/users/{id}/password`, Users page) for deployments without SMTP. Verified with a local SMTP catcher: forgot password → e-mail → link → new password → sign-in
- [x] Full build (0 warnings target), all tests green — 0 warnings; 757 tests (715 unit, 42 integration)
- [x] Run against PostgreSQL (16, docker compose `db`): no pending model changes, both migrations applied, demo data seeded; 39 browser checks (respondent, reports, admin) + 42 REST API checks (incl. every export format) + authorization spot-check (16) all green
- [x] Code review + security review → fixes: (1) DI bug — the e-mail sender was scoped but `MapIdentityApi` resolves it from the root provider (crash in Development) → singleton, and the integration host now runs with `ValidateScopes`/`ValidateOnBuild`; (2) expected API errors (400/403/404/409/422) were logged as errors with stack traces by the .NET 8 exception middleware → `ApiErrorFilter` on `/api/v1` + regression test; (3) interactive submissions bypassed the per-IP limiter → per-circuit `SubmissionThrottle` (5/min; deliberately not per IP because of shared NAT addresses); (4) `nosniff` + referrer policy on every response (`SecurityHeadersMiddleware`); (5) `dotnet list package --vulnerable`: AngleSharp advisory via bUnit 1.40 → bUnit 2.11.3 (tests migrated); now clean. Verified: admin pages reject guests/users, services re-check admin, SVG logos screened + sandboxed, no links in production logs

### Phase 8 — DevOps & docs
- [ ] Dockerfile + docker-compose (app + postgres)
- [ ] GitHub Actions CI
- [ ] `docs/DOCUMENTATION.md` complete (architecture, schema, API, UI, reports, exports, examples, ops)
- [ ] README, CHANGELOG release entry

## 8. Conventions (for humans and agents)

* Namespaces = folder paths (`SmartSurvey.Application.Surveys`, …). File-scoped namespaces.
* Every public type/member gets an XML doc comment; non-obvious logic gets inline comments.
* No business logic in Blazor components or endpoints — call Application services.
* Services throw `NotFoundException`, `AppValidationException`, `ForbiddenException`,
  `ConflictException`, `BusinessRuleException`; the API maps them to ProblemDetails
  (404/400/403/409/422), the UI shows them as alerts.
* Always use `TimeProvider` for "now"; store UTC.
* No new NuGet packages without updating `Directory.Packages.props` and this plan.
* Commit after each completed phase: `git commit -m "Phase N: …"`. Update CHANGELOG `[Unreleased]`.
* Dev credentials (seeded, Development only): `admin@smartsurvey.local / Admin123!`,
  `user@smartsurvey.local / User123!`.

## 8b. UI design standard ("user friendly, attractive, professional")

* Design system lives in `src/SmartSurvey.Web/wwwroot/app.css` (Bootstrap 5.3.8 + Bootstrap Icons 1.13 + Inter,
  all self-hosted under `wwwroot/lib`). Indigo primary (#4f46e5), slate neutrals, soft shadows, 12px radii,
  full **dark mode** (`data-bs-theme`, toggle in the top bar, remembered in localStorage).
* Layouts: `MainLayout` (public: sticky glass navbar + footer) and `AdminLayout` (dark sidebar + top bar).
  Pages under `Components/Pages/Admin` automatically get `AdminLayout` + `[Authorize(Policy = "Admin")]`.
* Always use the shared components in `Components/Shared`: `PageHeader`, `StatusBadge`, `EmptyState`,
  `LoadingSpinner`, `StatCard`, `ErrorAlert`, `Modal`, `ConfirmDialog`, `Pager`, `LocalDateTime`,
  `ChartView`; `ToastService` for feedback on admin pages; `BrowserInterop` for downloads/clipboard/time zone.
* Every list has an empty state, every async load a spinner/skeleton, every destructive action a confirm dialog,
  every save a toast; forms show inline validation messages; buttons show a spinner while busy; mobile-first
  responsive grids; icons on primary actions; keyboard accessible (labels, aria attributes, focus states).
* Key CSS classes: `.page-header`, `.card-hover`, `.stat-card`, `.kpi-grid/.kpi`, `.badge-status`, `.table` (styled),
  `.hero`, `.feature-icon`, `.eyebrow`, `.gradient-text`, `.section`, `.survey-shell`, `.survey-hero`,
  `.question-card`, `.choice-item(.selected)`, `.rating-stars`, `.scale-options`, `.builder-layout`,
  `.section-block`, `.question-editor(.active)`, `.type-picker`, `.option-row`, `.logic-rule`, `.chart-container`,
  `.code-box`, `.qr-box`, `.coffee-card`, `.btn-coffee`, `.auth-card`, `.btn-soft-*`, `.btn-icon`, `.status-icon`,
  `.survey-welcome`, `.thank-you-message`, `.text-pre-line`, `.embed-main`, `.preview-stage/.preview-phone`.
* Reports UI: `Components/Admin/Reports` — `ReportDesign` (pure widget/filter operations + `ReportErrors` mapping;
  unit-tested), `WidgetEditor`, `ReportFiltersEditor`, `ReportView`/`WidgetView`/`DataTable` (shared by viewer and
  live preview), `ExportMenu`.
* Survey runner: `Components/Runner` — `SurveyRunState` (pure state machine: pages, logic, validation, option order,
  requests; unit-tested), `QuestionField` (one question per type), `SurveyRunner` (live / embedded / preview modes),
  `ThankYouCard`.

## 8c. Quality gates

* `dotnet build SmartSurvey.sln` — 0 errors, 0 warnings.
* `dotnet test` — **all** unit + integration tests green before any phase is marked done.
* Every service/engine/exporter has unit tests (happy path + validation + edge cases); API routes have
  integration tests; key Blazor components have bUnit tests.
* Shared test helpers: `tests/SmartSurvey.UnitTests/TestSupport` (`SqliteTestDatabase`, `TestCurrentUser`,
  `RecordingAuditService`, `SampleSurveys`). Put feature-specific helpers in the feature's test folder/namespace.
* Provider-portable LINQ only (tests run on SQLite, production on PostgreSQL): no `EF.Functions.ILike`, use
  `x.Prop.ToLower().Contains(term.ToLower())`; no raw SQL in services.

## 9. Progress Log

### Working mode (read when resuming)

* **No multi-agent workflows** (user instruction, 2026-09-27): all remaining work is done directly in the main
  session, one task at a time, on `main`. Do not use the Workflow tool, subagents, parallel worktree tracks or
  separate review/fix agent rounds. Verification = unit/integration tests + `scripts/smoke.sh` + manual checks.
* Phase 6 "tracks" are now just the order of work: 6A → 6B → 6C → 6D → 6E.
* Keep reads targeted (grep / line ranges) to limit usage.
* History: Phases 4A/4B were built by parallel agents in worktrees under `../SmartSurvay-worktrees/`; the saved
  scripts in `.claude/workflows/` are kept for reference only and are no longer run.
* `scripts/smoke.sh` boots the app on SQLite with demo data and checks pages (admin login included).

### Log

| Date | Phase / task | Notes |
|---|---|---|
| 2026-09-26 | Phase 0 | .NET 8 SDK installed, git repo initialised, plan + changelog created |
| 2026-09-27 | Phase 0 | Docker Desktop engine unavailable (WSL1) → PostgreSQL 16.15 installed natively via winget (service `postgresql-x64-16`, superuser `postgres/postgres`); role+db `smartsurvey/smartsurvey` created |
| 2026-09-27 | Phases 1–3 | Solution scaffolded (6 projects, CPM, dotnet-ef 8.0.31 local tool); domain model, AppDbContext + configurations, InitialCreate migration (applied to PostgreSQL); application contracts, LogicEvaluator, ResponseValidator; web host (auth cookie+bearer, policies, ProblemDetails, rate limiting, health, Swagger); design system + layouts + shared components; stubs for Phase 4 services; test support + 72 passing core tests; app boots against PostgreSQL |
| 2026-09-27 | Phase 4 (run 1) | Parallel worktrees (harness worktree isolation failed → manual `git worktree add` under `../SmartSurvay-worktrees`). 4A + 4B implemented (285/222 tests) and merged; 4C/4D interrupted by a usage limit (partial work kept in their worktrees) |
| 2026-09-27 | Branding | New user request: admin-customisable product name/tagline/icon/logo. Entity + `AddBrandingSettings` migration, cached `IBrandingService`, `/branding/logo` + `/branding/favicon`, `BrandMark` component; 457 unit tests green. Admin page + API assigned to Phase 5/6 (tracks 5-api, 6E) |
| 2026-09-27 | Phase 4 (run 2) | Workflow `phase4-complete`: finish 4C/4D, independent review+fix of 4A–4D (incl. PostgreSQL query checks). Workflow scripts saved in `.claude/workflows/` (`phase4-complete.js`, next `phase56-api-and-ui.js`; args documented at the top of §9) |
| 2026-09-27 | Working mode | User asked to stop multi-agent workflows (usage). Workflow run 2 stopped mid-4C/4D; partial files kept. Remaining work continues directly on `main`, sequentially (see "Working mode" above). Login page fix: internal CookieOrBearer scheme hidden from external-login list |
| 2026-09-27 | Phase 4 done | 4C finished directly: ReportService, PDF/CSV/TXT/XLSX/JSON exporters, raw response export (QuestPDF configured by the PDF exporter). Tests added for 4C (79) and 4D (28: audit, dashboard, users with real Identity, seeder). 564 unit tests green; seeder verified on PostgreSQL (admin login + demo data). Next: Phase 5 (REST API) |
| 2026-09-27 | Phase 5 done | REST API: 8 groups + branding under `/api/v1` (Api/*Endpoints.cs), optional query records for list endpoints (`ApiQueries.cs`), logo upload as base64 JSON (CSRF-safe without antiforgery), `docs/examples` (.http walkthrough + survey/response/report JSON). 35 integration tests (auth 401/403, ProblemDetails, lifecycle, public flow, exports, reports, admin, branding, OpenAPI, examples). Next: Phase 6A |
| 2026-09-27 | Phase 6A done | Home (branded hero, features, how it works), FAQ (grouped accordion + GET search), Buy Me a Coffee (Support options), /surveys (cards, guest prompt, search), /my/responses, account pages restyled (AuthCard, AccountLayout container, settings layout). Fixes: login now counts failures toward lockout, password minimum 8 on all forms, self-registered users get the User role, last sign-in recorded, display name claim in the user menu, BrowserInterop null time zone, dead scoped-CSS link. 14 bUnit tests; 613 tests green; smoke OK. Next: 6B builder |
| 2026-09-27 | Phase 6B done | Admin survey list (filters, paging, lifecycle actions, duplicate/template/export/import/delete), builder (`/admin/surveys/new` creates on first save; pages, questions, options incl. "Other" + paste list, per-type settings, question/page display logic, outline, server-error mapping to questions, unsaved-changes guard, optimistic-concurrency reload), share page (link, QR SVG/PNG, invitation text). `SurveyDesign` operations + 24 new tests; 637 tests green. Next: 6C runner |
| 2026-09-27 | Phase 6C done | Survey runner `/s/{slug}` (prerendered → interactive with persisted session + shuffle seed; live logic, per-page and final validation with live error clearing, progress by answered questions, question numbering along the path, pages whose questions are all hidden skipped, drafts auto-saved on page change/leave and via "Save & finish later", resume with "Welcome back"/start over, guest leave warning, server validation mapped to questions), thank-you page (`GetCompletionAsync`), admin preview (desktop/phone), embedding (`/embed/s/{slug}`, `EmbedLayout`, `SecurityHeadersMiddleware`, `Embedding` options, share-page snippet). 58 new tests (695 green); smoke OK; verified in headless Chrome (guest submit, draft resume, preview, shuffle stability, third-party iframe allowed only for /embed). Next: 6D reports UI |
| 2026-09-27 | Phase 6D done | Reports UI: list, builder with live preview (600 ms debounce, preview only once interactive, runs on a deep copy), viewer with exports and print. 27 new tests (722 green). Verified in headless Chrome: viewer charts, real PDF/XLSX downloads, overview start, live preview follows widget changes, create → edit address → view. Next: 6E |
| 2026-09-27 | Phase 6E done | Admin pages above. Browser check found a real issue: create-user fields bound on `change` lost the password when the button was triggered without blurring the field → bound on input. 15 new tests (737 green); verified in headless Chrome (dashboard, response filters, CSV export, detail, create/lock user, audit entries, branding save refreshes sidebar/title, reset). **Phase 6 complete.** Next: Phase 7 |
| 2026-09-27 | Phase 7: e-mail | `IEmailTransport` (Application) + `SmtpEmailTransport` (MailKit), branded `IdentityEmailSender`, dev-only confirmation link without SMTP, admin set-password. 16 new tests (754 green). Verified against a local SMTP catcher in Production mode (reset link works; no links in the log) |
| 2026-09-27 | Phase 7 done | Environment is now macOS: PostgreSQL 16 runs from the project's docker-compose (`docker compose up -d db`). Full verification against PostgreSQL (UI + API), review findings fixed (see Phase 7 checklist). 757 tests green, 0 warnings, no vulnerable packages. Next: Phase 8 |
