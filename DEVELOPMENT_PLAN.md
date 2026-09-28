# SmartSurvey — Development Plan

> **This file is the single source of truth for project progress.**
> If a Claude Code session is renewed/reset, read this file first (especially
> [§ 9 Progress Log](#9-progress-log) and the phase checklists), then `git log --oneline`,
> and continue with the first unchecked task. Update this file after every completed task/phase.

> **✔ LATEST WORK (2026-09-28): v2 follow-ups → 2.1.0 on branch `feature/v2-follow-ups` — all phases done**
> ([§ 11](#11-v2-follow-ups--branch-featurev2-follow-ups)), plus post-phase fixes (Blazor script in the container, account
> switching after logout), the account settings redesign and a documentation refresh — see the last rows of § 11.3.
> Pushing, PR and tag `v2.1.0` are the user's call. For new work, start a new section with the § 10.0 protocol.
> Phases 0–8 = v1.0.0, § 10 = v2.0.0, § 11 = v2.1.0 (all done).

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
| Runtime | **.NET 10 LTS / ASP.NET Core 10** (`net10.0`, C# 14) | Pinned via `global.json` (SDK 10.0.3xx). TFM centralised in `Directory.Build.props`. Upgraded from .NET 8 in § 11 Phase 17 (2026-09-28); .NET 10 is supported until November 2028. |
| Frontend | **Blazor Web App, Interactive Server** render mode (per-page interactivity) | Static SSR for public/marketing pages + Identity pages; `@rendermode InteractiveServer` for builder, runner, reports, admin. |
| ORM | **EF Core 10.0.12** | Code-first, migrations in Infrastructure. `IDbContextFactory` pattern (Blazor Server safe). |
| Database | **PostgreSQL 16** (Npgsql EF 10.0.3) | Alternative provider **SQLite** (config switch) for demos/tests (uses `EnsureCreated`). |
| Auth | **ASP.NET Core Identity** (Guid keys) — cookie for UI, **Identity bearer tokens** for REST API (`MapIdentityApi`) | Roles: `SuperAdmin` (no workspace), `Admin`, `User` (per workspace); claim `workspace_id`. |
| API | **Minimal APIs** grouped under `/api/v1`, **Swagger** (Swashbuckle 10) | ProblemDetails error contract. |
| Validation | **FluentValidation 12** | Request DTO validation inside services. |
| PDF | **QuestPDF 2026.9.x** (Community license) | Charts embedded as SVG. |
| XLSX (extra) | **ClosedXML 0.105** | |
| QR codes (extra) | **QRCoder 1.8** | Share page. |
| E-mail | **MailKit 4.18** | SMTP delivery of account e-mails (`Email:*` settings). |
| Encryption at rest | **ASP.NET Core Data Protection** via EF Core value converters | Respondents' written answers, "Other" texts and user agents (`Encryption:Enabled`); keys in the key ring, not the database. |
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
Workspace (auditable)                  Name, Slug(unique), Description, ContactEmail, Status(Active|Disabled|PendingApproval),
                                       StatusReason, StatusChangedAt, AllowSelfRegistration, ShowPublicSurveyList, OwnerId
PlatformSettings (single row)          AllowWorkspaceSignup, RequireWorkspaceApproval, SupportEmail
ApplicationUser : IdentityUser<Guid>   WorkspaceId? (null = super admin), DisplayName, CreatedAt, LastLoginAt, IsActive
(every entity below: WorkspaceId via IWorkspaceOwned; AuditLogEntry.WorkspaceId nullable = system event)
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
- [x] **Workspaces (v2.0, § 10):** isolated tenants with own admins/members, self-service sign-up (optional approval), join link, workspace page `/w/{slug}`, workspace settings; **super admin** System console (workspaces, accounts, branding, settings, system audit log); branding moved to super admins

(Checked items = in scope; see phase checklists for implementation status.)

## 6. Routes

### UI (Blazor)
| Route | Access | Purpose |
|---|---|---|
| `/` | public | Landing page / features |
| `/faq` | public | FAQ |
| `/guide` | public | User guide for non-technical users (screenshots in `wwwroot/img/guide`) |
| `/buy-me-a-coffee` | public | Support page (username configurable: `Support:BuyMeACoffeeUsername`) |
| `/surveys` | members | The member's workspace's open surveys (guests: link / workspace finder) |
| `/signup` | public | Create a workspace (creator becomes its admin) |
| `/w/{slug}` | public | Workspace page with its public surveys, join / sign in |
| `/workspace-unavailable` | public | Shown to members of disabled / pending workspaces |
| `/s/{slug}` | per survey | Take survey (multi-page runner) |
| `/s/{slug}/thank-you` | per survey | Completion page |
| `/embed/s/{slug}`, `/embed/s/{slug}/thank-you` | per survey | Runner / completion page for iframes on other sites (minimal layout; see `Embedding:*`) |
| `/my/responses` | user | My submissions & drafts |
| `/Account/*` | public | Identity (login, manage, 2FA …); `/Account/Register?workspace={slug}` = join a workspace |
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
| `/admin/audit` | admin | Audit log (the workspace's) |
| `/admin/settings` | admin | Workspace settings (name, description, contact, join link, public page) |
| `/system` | super admin | System overview (figures, pending approvals) |
| `/system/workspaces`, `/system/workspaces/new`, `/system/workspaces/{id}` | super admin | Workspaces: list, create, detail (approve, disable, enable, edit, delete) |
| `/system/accounts` | super admin | Every account (+ create super admins / members) |
| `/system/branding` | super admin | Product name, tagline, icon / logo (moved from `/admin/branding`) |
| `/system/settings`, `/system/audit` | super admin | System settings, system audit log |

### REST API (`/api/v1`, cookie or bearer auth)
| Method & Route | Access | Purpose |
|---|---|---|
| `POST /api/auth/login`, `/refresh` … | public | Identity API endpoints (bearer tokens); `/register` is refused (403) — see workspace sign-up/join |
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
| `GET /api/v1/public/surveys/{slug}` | per survey | Survey session (definition + draft + eligibility); header `X-Survey-Access-Key` for password-protected surveys |
| `POST /api/v1/public/surveys/{slug}/unlock` | per survey | Check a survey password → access key (rate-limited) |
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
| `GET /api/v1/public/branding`, `PUT /api/v1/branding`, `POST/DELETE /api/v1/branding/logo`, `POST /api/v1/branding/reset` | public / super admin | Branding |
| `GET/PUT /api/v1/workspace` | member / admin | Own workspace settings |
| `/api/v1/system/{overview,workspaces…,accounts…,settings,audit}` | super admin | System administration (see § 10.3) |
| `GET /api/v1/public/settings`, `GET /api/v1/public/workspaces/{slug}`, `POST /api/v1/public/workspaces`, `POST /api/v1/public/workspaces/{slug}/register` | public | Sign-up / join (rate limited) |
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
- [x] Dockerfile + docker-compose (app + postgres) — verified on Docker 29: image builds, `docker compose up` runs the stack on PostgreSQL 16, `scripts/container-smoke.sh` passes (health, pages, admin sign-in, 143 KB PDF export → QuestPDF works in the Linux image), browser check of the published build (interactive runner, admin dashboard, no failed requests). Fixed: `ASPNETCORE_URLS` → `ASPNETCORE_HTTP_PORTS` (start-up warning)
- [x] GitHub Actions CI — added gates: known vulnerable packages, model changes without a migration (`has-pending-model-changes`), container smoke test (image loaded, `docker compose up --no-build`, logs, teardown). YAML validated locally; runs on GitHub on the next push
- [x] `docs/DOCUMENTATION.md` complete (architecture, schema, API, UI, reports, exports, examples, ops) — updated for runner, reports UI, admin pages, embedding, e-mail, security hardening, cross-platform setup, CI and container smoke
- [x] README, CHANGELOG release entry — `[1.0.0] - 2026-09-27`

### Backlog (after 1.0)
- [x] **Move to .NET 10 LTS before .NET 8 support ends on 2026-11-10** — done in § 11 Phase 17 (branch `feature/v2-follow-ups`)
- [x] Optional: keep the headless-browser checks in the repository — `scripts/browser` (workspace flows + screenshots)
- [ ] Optional: bot protection (CAPTCHA / WAF) for very public surveys — interactive submissions are throttled per connection, the API per IP

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
* Dev credentials (seeded, Development only): `admin@smartsurvey.local / Admin123!` (admin of "Default workspace"),
  `user@smartsurvey.local / User123!`, `superadmin@smartsurvey.local / SuperAdmin123!`, `admin@acme.local / Admin123!`
  (second demo workspace "Acme Research").

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
  session, one task at a time, on the current work branch (v2: `feature/multi-workspace`). Do not use the Workflow tool, subagents, parallel worktree tracks or
  separate review/fix agent rounds. Verification = unit/integration tests + `scripts/smoke.sh` + manual checks.
* Phase 6 "tracks" are now just the order of work: 6A → 6B → 6C → 6D → 6E.
* Keep reads targeted (grep / line ranges) to limit usage.
* History: Phases 4A/4B were built by parallel agents in worktrees under `../SmartSurvay-worktrees/`; the
  workflow scripts they used were removed from the repository (2026-09-28).
* `scripts/smoke.sh` boots the app on SQLite with demo data and checks pages (admin login included).

### Log

| Date | Phase / task | Notes |
|---|---|---|
| 2026-09-26 | Phase 0 | .NET 8 SDK installed, git repo initialised, plan + changelog created |
| 2026-09-27 | Phase 0 | Docker Desktop engine unavailable (WSL1) → PostgreSQL 16.15 installed natively via winget (service `postgresql-x64-16`, superuser `postgres/postgres`); role+db `smartsurvey/smartsurvey` created |
| 2026-09-27 | Phases 1–3 | Solution scaffolded (6 projects, CPM, dotnet-ef 8.0.31 local tool); domain model, AppDbContext + configurations, InitialCreate migration (applied to PostgreSQL); application contracts, LogicEvaluator, ResponseValidator; web host (auth cookie+bearer, policies, ProblemDetails, rate limiting, health, Swagger); design system + layouts + shared components; stubs for Phase 4 services; test support + 72 passing core tests; app boots against PostgreSQL |
| 2026-09-27 | Phase 4 (run 1) | Parallel worktrees (harness worktree isolation failed → manual `git worktree add` under `../SmartSurvay-worktrees`). 4A + 4B implemented (285/222 tests) and merged; 4C/4D interrupted by a usage limit (partial work kept in their worktrees) |
| 2026-09-27 | Branding | New user request: admin-customisable product name/tagline/icon/logo. Entity + `AddBrandingSettings` migration, cached `IBrandingService`, `/branding/logo` + `/branding/favicon`, `BrandMark` component; 457 unit tests green. Admin page + API assigned to Phase 5/6 (tracks 5-api, 6E) |
| 2026-09-27 | Phase 4 (run 2) | Workflow `phase4-complete`: finish 4C/4D, independent review+fix of 4A–4D (incl. PostgreSQL query checks). Workflow scripts were kept in `.claude/workflows/` until they were removed on 2026-09-28 |
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
| 2026-09-27 | Phase 8 done — **v1.0.0** | Container stack verified end to end (smoke script + browser), Dockerfile port warning fixed, CI gates added (vulnerable packages, pending migrations, container smoke), documentation/README brought up to date, CHANGELOG 1.0.0. All phases complete; see the Backlog above (.NET 10 migration before 2026-11-10). Local `main` is ahead of `origin` until pushed |

---

## 10. Multi-workspace (v2.0) — branch `feature/multi-workspace`

User request (2026-09-28): the site hosts many **workspaces** — fully isolated rooms that never share data, each
with its own admin(s). Anyone can create a workspace and becomes its admin. A new **super admin** manages
workspaces (enable/disable), accounts, branding and system settings. Workspace admins keep every current admin
feature except branding, plus their own workspace settings.

**Terminology.** "Workspace" is the right user-facing word (Slack, Notion, Asana use it for exactly this). The
engineering term is *tenant* (multi-tenancy); code and UI both say *workspace* so there is only one word.
"Super user" becomes the role **`SuperAdmin`**, shown as "Super admin"; its area is called *System*.

### 10.0 Resume protocol (read first after any interruption)

1. `git checkout feature/multi-workspace` · `git status` · `git log --oneline -10`.
   Uncommitted changes = the task marked `[~]` below was interrupted: inspect the diff, finish it, don't restart it.
2. Read § 10.1 (decisions) and the **last row of § 10.5** (it always names the next step).
3. Continue with the first `[~]` (else first `[ ]`) task in § 10.4. Mark a task `[~]` when starting it.
4. After **each task**: tick it, add a § 10.5 row ending in "Next: …", commit
   (`Phase N (wip): <task>` is fine) — an interruption then loses at most one task.
5. **Phase gate** (every phase, in this order):
   `dotnet build SmartSurvey.sln` (0 warnings) → `dotnet test` (all green) →
   `docker compose up -d --build` (user instruction: containers rebuilt and **left running** after every phase) →
   `scripts/container-smoke.sh` → commit `Phase N: …` → CHANGELOG `[Unreleased]`.
   The compose volume holds real v1.0 data, so each rebuild also exercises the upgrade migration.
   Never push to `origin` unless asked.
6. Working mode from § 9 still applies: no subagents/workflows, targeted reads, work directly on this branch.

### 10.1 Decisions (defaults chosen by the agent; the user may revise)

| # | Decision |
|---|---|
| D1 | **One account = one workspace.** E-mail stays unique system-wide (Identity login is global). A person in two workspaces uses two e-mail addresses. Super admins belong to no workspace. |
| D2 | **Survey links stay `/s/{slug}`**; slugs stay unique system-wide, so existing links, QR codes and embeds keep working. The slug check is the only cross-workspace query and returns just "taken / free". |
| D3 | **Super admin never sees workspace content** (surveys, responses, reports, workspace audit log). They see workspace metadata and aggregate counts, and manage workspaces, accounts, branding, system settings, system audit log. |
| D4 | **Branding is system-wide, super admin only.** Workspace admins get `/admin/settings`: name, description, contact e-mail, "people can join with the workspace link" (self-registration), "show public survey page". |
| D5 | **Self-service sign-up** (`/signup`): new workspace + creator as its admin. Super admin can switch sign-up off or require approval (status `PendingApproval`). |
| D6 | **Disabled workspace:** members cannot sign in; open sessions and API tokens are refused (≤ 1 min, cached status); its survey links show "not available"; data is kept. Delete is allowed only for a disabled workspace (type-the-name confirmation). |
| D7 | **Upgrade from v1.0:** a migration creates "Default workspace" (slug `default`) only when data exists and moves all data and users into it; existing admins become its admins. A super admin is created at start-up from `Seed:SuperAdminEmail/SuperAdminPassword` when none exists. |
| D8 | **Isolation is enforced in the data layer, fail-closed:** every tenant-owned table has `WorkspaceId`; EF global query filters use the context's `DataScope` (`None` → sees nothing, `Workspace(id)`, `System` → unfiltered, used only by platform code); `SaveChanges` stamps `WorkspaceId` on inserts and throws on cross-workspace writes. Services additionally check roles as today. |
| D9 | **Respondents:** members sign in and answer their own workspace's surveys. A signed-in member of *another* workspace is treated as a guest on anonymous surveys (no link to their account) and refused on sign-in-only surveys. |
| D10 | `POST /api/auth/register` (Identity API) is disabled — it would create accounts outside any workspace. Replaced by `POST /api/v1/public/workspaces` (sign-up) and `POST /api/v1/public/workspaces/{slug}/register` (join). |
| D11 | Release as **2.0.0** (roles, routes and the register endpoint change). |

### 10.2 Data model changes

```
Workspace (auditable)        Name, Slug(unique), Description?, ContactEmail?, Status(Active|Disabled|PendingApproval),
                             StatusReason?, StatusChangedAt?, AllowSelfRegistration(=true), ShowPublicSurveyList(=true), OwnerId?
PlatformSettings (singleton) AllowWorkspaceSignup(=true), RequireWorkspaceApproval(=false), SupportEmail?
ApplicationUser              + WorkspaceId? (null = super admin)            — no global filter (Identity needs global lookups);
                                                                             filtered only inside a Workspace scope
ITenantOwned.WorkspaceId     Survey, SurveySection, Question, QuestionOption, LogicRule, LogicCondition, SurveyResponse,
                             Answer, AnswerSelection, ReportDefinition, ReportWidget  (non-null, indexed, FK → Workspace)
AuditLogEntry                + WorkspaceId? (null = system event, visible only to super admins)
AppRoles                     + SuperAdmin
```

### 10.3 Routes added / changed

UI: `/signup` (create workspace) · `/Account/Register?workspace={slug}` (join; without a workspace it explains the
options) · `/w/{slug}` (workspace page + public surveys) · `/workspace-unavailable` · `/admin/settings` (workspace
settings) · `/admin/branding` removed → `/system/branding` · `/system` (overview) · `/system/workspaces`,
`/system/workspaces/{id}` · `/system/accounts` · `/system/settings` · `/system/audit`.

API: `GET/PUT /api/v1/workspace` (admin: own settings) · `/api/v1/system/workspaces` (GET list, POST create,
GET/PUT `{id}`, POST `{id}/enable|disable|approve`, DELETE `{id}`) · `/api/v1/system/accounts` (list, create super
admin/member, roles, password, lock/unlock, delete) · `GET/PUT /api/v1/system/settings` · `GET /api/v1/system/overview` ·
`GET /api/v1/system/audit` · branding write endpoints → super admin · `GET /api/v1/public/workspaces/{slug}` ·
`POST /api/v1/public/workspaces` · `POST /api/v1/public/workspaces/{slug}/register` · `GET /api/v1/public/surveys?workspace=`.

### 10.4 Phases & tasks

#### Phase 9 — Workspace data model, isolation core, upgrade migration
- [x] 9.1 Domain: `Workspace`, `WorkspaceStatus`, `PlatformSettings`, `ITenantOwned`; `WorkspaceId` on the entities in § 10.2; `AppRoles.SuperAdmin`
- [x] 9.2 Persistence: configurations + indexes, `DataScope` on `AppDbContext`, global query filters, SaveChanges workspace guard/stamping; `IAppDbContextFactory.CreateAsync` (current user's workspace) / `CreateForWorkspaceAsync` / `CreateSystemAsync`; `ICurrentUser.WorkspaceId` + `IsSuperAdmin` (claim `workspace_id`)
- [x] 9.3 Keep existing behaviour working inside one workspace: public survey flows open the survey's workspace, slug check system-wide, seeder puts admin/demo data into a default workspace
- [x] 9.4 Migration `AddWorkspaces` with data backfill into "Default workspace" (PostgreSQL); `has-pending-model-changes` clean
- [x] 9.5 Test support (default test workspace, scoped seeding, `TestCurrentUser.WorkspaceId`); existing suite green; new data-layer isolation tests (filters, write guard, fail-closed `None` scope)
- [x] 9.6 Phase gate (§ 10.0 step 5) — verify the v1.0 compose database upgrades (data lands in Default workspace, admin still signs in)

#### Phase 10 — Workspace-aware services + platform services
- [x] 10.1 Review/adjust every service for scoping: surveys (duplicate/templates/import), responses (D9, my responses), reports + engine, exports, dashboard, audit (workspace vs system events)
- [x] 10.2 `UserAdminService` scope rules: workspace admin → own workspace, roles ⊆ {Admin, User}, last-admin protection; super admin → all accounts, create super admins / workspace members
- [x] 10.3 `IWorkspaceService` (own settings, public lookup by slug) + `IWorkspaceStatusProvider` (cached status, invalidated on change)
- [x] 10.4 `IPlatformWorkspaceService` (list with counts, create with admin account, update, enable/disable/approve, delete disabled) + `IPlatformSettingsService` + overview stats
- [x] 10.5 `IWorkspaceSignupService`: sign-up (workspace + owner admin, approval setting) and join (member registration when allowed)
- [x] 10.6 Seeder: super admin bootstrap (`Seed:SuperAdmin*`), demo data in "Demo workspace" + second demo workspace (`admin@acme.local`) to show isolation; `.env.example`/compose variables
- [x] 10.7 Unit tests: cross-workspace isolation for every service, platform services, sign-up/join rules
- [x] 10.8 Phase gate

#### Phase 11 — Web host, authentication, REST API
- [x] 11.1 Claims (`workspace_id`), policies (`Admin` requires a workspace, `SuperAdmin`), `AppSignInManager.CanSignInAsync` (disabled/pending workspace), access middleware for open cookies/bearer tokens, circuit revalidation (1 min, cached)
- [x] 11.2 Disable Identity `/api/auth/register`; public workspace endpoints (info, sign-up, join; rate limited)
- [x] 11.3 `/api/v1/workspace`, `/api/v1/system/*`; branding writes → super admin; public surveys `?workspace=`
- [x] 11.4 `docs/examples` (.http) for the new endpoints; integration tests: two workspaces can't see each other (every endpoint group), disabled workspace (login, existing token, public link), role boundaries (admin ↛ system/branding, super admin ↛ survey data)
- [x] 11.5 Phase gate

#### Phase 12 — UI: public pages + workspace admin
- [x] 12.1 `/signup` page, register/join page with workspace context, `/w/{slug}`, `/workspace-unavailable`, `/surveys` per workspace, home CTA, navbar/user menu (workspace name, System link)
- [x] 12.2 Runner: sign-in-only survey from another workspace → friendly message; register link carries the workspace
- [x] 12.3 Admin: sidebar shows the workspace, `/admin/settings`, branding removed, users page scoped + join link (copy), last-admin guard on account self-deletion
- [x] 12.4 bUnit tests; Phase gate

#### Phase 13 — UI: System (super admin) console
- [x] 13.1 `SystemLayout` + nav, `/system` overview (KPIs, recent workspaces, pending approvals)
- [x] 13.2 Workspaces list/detail/create, enable/disable (reason), approve, delete (type-to-confirm)
- [x] 13.3 Accounts (search, workspace/role filters, lock/unlock, set password, roles, create super admin, delete)
- [x] 13.4 `/system/branding` (moved page), `/system/settings`, `/system/audit`
- [x] 13.5 bUnit tests; Phase gate

#### Phase 14 — Verification & release 2.0.0
- [x] 14.1 Full build/tests; PostgreSQL run incl. upgrade of a v1.0 database; `scripts/smoke.sh` + `container-smoke.sh` extended (super admin, second workspace)
- [x] 14.2 Headless-browser checks: sign-up → own workspace; two workspaces isolated; disable → members locked out, links unavailable; enable; super admin console
- [x] 14.3 Security review focused on cross-workspace access (IDOR via ids in URLs/API, filters bypassed, circuits), fixes
- [x] 14.4 Docs: DOCUMENTATION.md (tenancy model, roles, routes, upgrade notes), README, user guide + FAQ, CHANGELOG 2.0.0, § 4–6 of this file
- [x] 14.5 Final phase gate (containers running on the release build)

### 10.5 Log

| Date | Phase / task | Notes |
|---|---|---|
| 2026-09-28 | Plan | Branch `feature/multi-workspace` created from `main` (v1.0.0). Plan written (§ 10). Note: `feature/password-protected-surveys` (5 commits, own migration) is not on `main`; merging it later needs its migration re-generated on top of `AddWorkspaces`. Next: 9.1 |
| 2026-09-28 | 9.1–9.3 | Domain (`Workspace`, `PlatformSettings`, `IWorkspaceOwned` on 11 entities, nullable on audit/users, `SuperAdmin` role); `DataScope` + global filters + save guard in `AppDbContext`; factory `CreateAsync/CreateForWorkspaceAsync/CreateSystemAsync`; `ICurrentUser.WorkspaceId/IsSuperAdmin` (claim `workspace_id`). Respondent flow resolves the survey's workspace (D9 + `OtherWorkspace`/`Unavailable` verdicts), `ListAvailableAsync(workspaceSlug)`, audit `LogInWorkspaceAsync`, system-wide slug check, seeder default workspace (`Seed:WorkspaceName/Slug`). 719 unit + 42 integration green. Next: 9.4 migration |
| 2026-09-28 | 9.4–9.5 | `AddWorkspaces` migration + v1.0 backfill, verified on a copy of the compose database (`smartsurvey_upgrade_test`: 4 surveys, 1915 answers, 48 users, 44 audit rows → Default workspace, FKs created) and on an empty database (no workspace created); no pending model changes. 11 isolation tests (`Core/WorkspaceIsolationTests`). 730 unit + 42 integration green, 0 warnings. |
| 2026-09-28 | **9.6 BLOCKED — needs the user** | The running compose stack was built from `feature/password-protected-surveys`: its database has that branch's migrations (`AddSurveyAccessPassword`, `EncryptedAnswerColumns`) and **encrypted** answer text. An image of this branch (based on `main`) would show ciphertext and write plaintext → do **not** run `docker compose up --build` from this branch yet. Proposed fix: rebase this branch onto `feature/password-protected-surveys` and regenerate `AddWorkspaces` on top (the agent's rebase was refused by the permission check, so the user decides). Next: user's decision → then 9.6 |
| 2026-09-28 | Merge `main` | Blocker resolved: the user merged PR #1 (password-protected surveys, bot protection, encryption at rest) into `main` and asked to merge `main` here. Conflicts resolved (eligibility enum: `PasswordRequired = 8`, `OtherWorkspace = 9`, `Unavailable = 10`; encryption + workspace filters both in `AppDbContext`; access keys + workspace targets in `ResponseService`). Fixed on top: `UnlockAsync` resolves the survey's workspace (guests could not unlock otherwise) and bot challenges go to everyone answering as a guest, incl. members of other workspaces. `AddWorkspaces` still sorts after main's migrations; its Designer synced to the merged snapshot; no pending model changes. 757 unit + 45 integration green. Next: 9.6 phase gate (back up the compose DB first) |
| 2026-09-28 | **Phase 9 done** | Gate: build 0 warnings, 757 unit + 45 integration green. Compose DB backed up (`pg_dump -Fc`, session scratchpad), `docker compose up -d --build`: `AddWorkspaces` applied to the live data (4 surveys, 48 accounts → `default`), `SuperAdmin` role created, public survey eligible, no warnings in the logs; containers left running. Found + fixed: `FieldEncryptionMigrator` (from main) looped forever in the fail-closed scope → system scope + stop condition + ordered batches, regression test uses a `None`-scoped context. The compose admin's password is user-set (not `ChangeMe123!`): run the smoke with `''` as password to skip admin checks — every wrong attempt counts toward lockout. Next: 10.1 |
| 2026-09-28 | **Phase 10 done** | Services: branding writes super-admin only; `UserAdminService` scoped (workspace admins: own members, roles Admin/User, last admin per workspace; super admins: all accounts, create super admins/members, last super admin protected; reads in a scoped context → correct response counts); `IWorkspaceService` + `WorkspaceStatusCache` (30 s TTL, invalidated on change); `PlatformSettingsService`; `PlatformWorkspaceService` (list/detail with figures only, create with first admin, rename/re-address, enable/disable/approve logged in system + workspace log, delete disabled workspace with typed name, overview); `WorkspaceSignupService` (sign-up honours the system settings, join honours self-registration); `IAuditService.ListSystemAsync`; policies `SuperAdmin`/`ApiSuperAdmin`, `Admin`/`ApiAdmin` require a workspace claim; branding API → super admin (pulled forward from 11.3). Seeder: super admin bootstrap (`Seed:SuperAdmin*`, compose `SUPERADMIN_EMAIL/PASSWORD`, dev `SuperAdmin123!`), second demo workspace `acme` (`admin@acme.local`). 811 unit + 45 integration green. Gate on the live stack: super admin created (sign-in OK), `acme` seeded; API isolation verified on PostgreSQL (Acme admin: own survey/users only, other workspace's survey 404, branding 403; super admin: survey data 403). Next: 11.1 |
| 2026-09-28 | **Phase 11 done** | `AppSignInManager` (disabled/pending workspace → NotAllowed; **must be registered after `AddApiEndpoints`**, which re-registers the default sign-in manager — found by the integration tests), login page redirects to `/workspace-unavailable` only after verifying the password (no account probing), `WorkspaceAccessMiddleware` (existing cookies/tokens: API + `/_blazor` 403, pages sign out → `/workspace-unavailable`), circuit revalidation every minute (workspace, cached) + security stamp every 30 min, context factory refuses members of inactive workspaces. API: Identity `/api/auth/register` → 403 with pointers; `/api/v1/public/{settings,workspaces,workspaces/{slug},workspaces/{slug}/register}` (sign-up/join rate limited, confirmation e-mail via `AccountEmails`); `/api/v1/workspace` (GET members, PUT admins); `/api/v1/system/{overview,workspaces…,accounts…,settings,audit}` (super admins). `RateLimits:AuthPerMinute` (default 20). `.http` walkthrough extended (steps 16–23) + test that every documented request is an OpenAPI operation. 17 new integration tests (isolation per endpoint group, disable/enable, approval, sign-up off, role boundaries). 811 unit + 63 integration green; containers rebuilt; live PostgreSQL check (register 403, sign-up → isolated admin, disable → old token 403 + login 401, delete cleans up). Next: 12.1 |
| 2026-09-28 | **Phase 12 done** | Public UI: `/signup` (sign-up with approval/closed states; signs the founder in → `/admin`), `/Account/Register` (no workspace → two ways in; `?workspace=` → join form, invite-only and not-found states), `/w/{slug}` (intro, contact, public surveys via shared `SurveyCard`, join/sign-in or member badge), `/surveys` (members: own workspace; guests: link/workspace finder; super admins → System), `/workspace-unavailable`, home CTAs per role, user menu shows the workspace / "Super admin" and role links, top bar Admin/System buttons. Runner: session carries workspace name/slug → "Create account" joins that workspace, "More from …", `OtherWorkspace`/`Unavailable` texts. Admin: sidebar workspace name, `/admin/settings` (name, description, contact, join link with copy, public page), branding link removed (page moves to /system in 13.4), users page join link + role filter limited to workspace roles, last-admin guard on self-deletion. Workspace cache now holds name/slug (`WorkspaceInfo`). Tests found a real bug: an empty optional address failed `StringLength(MinimumLength)` → regex. 829 unit + 69 integration green (incl. form sign-up end to end); containers rebuilt; new pages 200 live. Next: 13.1 |
| 2026-09-28 | **Phase 13 done** | System console (`Pages/SystemConsole` — a folder named `System` shadows the `System` namespace): `SystemLayout` (tinted sidebar, "New workspace"), `/system` overview (KPIs, approve pending in place, newest workspaces), `/system/workspaces` (search/status, `WorkspaceTable`), `/system/workspaces/new`, `/system/workspaces/{id}` (figures, approve / disable with reason / enable, details incl. address, admins, delete only when disabled + typed name), `/system/accounts`, `/system/branding` (moved), `/system/settings`, `/system/audit`. Shared components instead of copies: `AccountManager` (Users page + Accounts, `SystemMode`: workspace column/filter, super admin accounts, create member-of-workspace or super admin) and `AuditLogView` (`SystemMode` → `ListSystemAsync`). Tests found a real bug: a bool-bound `<select>` never switched → string-bound account type. 12 bUnit + 5 HTTP tests (guests → login, workspace admin → AccessDenied on /system, super admin → AccessDenied on /admin, /admin/branding 404). 841 unit + 74 integration green; containers rebuilt; live cookie sign-in as super admin, all /system pages 200. Next: 14.1 |
| 2026-09-28 | 14.1–14.3 | Verification: `scripts/smoke.sh` gained `--user superadmin|acme` (all pages of all five roles render on SQLite without errors); `container-smoke.sh` checks sign-up/join/workspace pages, public settings, super admin sign-in, system overview, super admin ↛ survey data (optional 4th/5th args, '' skips). Headless browser (`scripts/browser/workspaces.check.js`, puppeteer-core + Chrome, fresh SQLite instance): 17 checks green, no console/request/5xx errors — sign-up → own empty workspace, settings save, join link, super admin disables (reason) → member redirected + login explains + workspace page gone, enable → sign-in again, super admin account via dialog, admin ↛ /system, runner across workspaces. Security review of every filter bypass (system scope only in seeding/startup, super-admin services, id/status/public-field lookups for share links, slugs and public workspace pages); fixes: `DisableAsync` checks the role before validating; `/signup` and `/Account/Register` rate limited like the API (test); `ALLOWED_HOSTS` in compose/.env (confirmation links use the request host — pre-existing, now documented). No vulnerable packages. Demo Acme survey got its own description. Next: 14.4 docs |
| 2026-09-28 | 14.4 | Docs: DOCUMENTATION.md (§5.3 tenancy model/roles/enforcement, data model, upgrade notes, API, UI guide incl. System console, security, configuration, testing, deployment, troubleshooting), README (highlights, accounts table, upgrade note, settings, dev credentials, gallery), user guide (Workspaces, Workspace settings, System console, super admin branding; new audience badge), FAQ (Workspaces group, admin answers), CHANGELOG `[2.0.0]` (restructured: main's post-1.0 items had landed under Changed), `Version` 2.0.0, plan §2/4/5/6/8. New screenshots (dashboard light/dark, users, branding, system-overview, workspace-settings) captured with puppeteer — reviewing them found a **real leak**: the workspace dashboard counted every account of the site (Users table unfiltered) → accounts now filtered inside a workspace scope (Identity keeps global look-ups on unscoped contexts), seeder's cross-workspace address check uses `IgnoreQueryFilters`; tests added. 842 unit + 75 integration green. Next: 14.5 final gate |
| 2026-09-28 | **Phase 14 done — v2.0.0** | Final gate: clean build 0 warnings, 842 unit + 75 integration green, no pending model changes; browser check on the final build 17/17, no browser errors; `docker compose up -d --build` on the live stack, container smoke incl. super admin checks all OK, no warnings in the log; dashboard fix verified on PostgreSQL (Acme admin: 1 user). Branch ready for review; not pushed. |

### 10.6 Follow-ups (not blocking v2.0)

- Merge: push `feature/multi-workspace`, PR into `main`, tag `v2.0.0` after merging (user decision).
- Guide screenshots other than dashboard/users/branding/settings/system still show the 1.x admin sidebar (a "Branding" link, no workspace name) — recapture with `scripts/browser` when convenient.
- Optional: a canonical public base URL setting for e-mail links (today: request host + `AllowedHosts`).
- Optional: starter survey templates for new workspaces (new workspaces start empty).
- Multi-instance deployments: workspace status changes reach other instances within the 30 s cache TTL.

---

## 11. v2 follow-ups — branch `feature/v2-follow-ups`

User request (2026-09-28): "fix those follow up pending things in a new branch" — the items of § 10.6 and the § 7
backlog item (.NET 10 before .NET 8 support ends on 2026-11-10). Pushing, PRs, tags and the live stack's passwords stay
with the user.

### 11.1 Protocol

As § 10.0 (resume from the last § 11.3 row, `[~]` markers, commit per task, phase gate = build 0 warnings → all tests →
`docker compose up -d --build` → `scripts/container-smoke.sh http://localhost:8080 admin@smartsurvey.local '' superadmin@smartsurvey.local 'ChangeMe123!'`
→ commit). Screenshots and browser checks always run against a fresh local SQLite instance, never the live database.

### 11.2 Phases & tasks

#### Phase 15 — Settings, starter templates, housekeeping
- [x] 15.1 Housekeeping: drop the scratch database `smartsurvey_upgrade_test`; move the pre-upgrade backup out of the session scratchpad to `../backups/` (outside the repository)
- [x] 15.2 `App:PublicBaseUrl` (Docker `PUBLIC_BASE_URL`): e-mail links (confirmation, reset, change e-mail — every flow, via the e-mail sender) and the join link use it when set
- [x] 15.3 `Workspaces:StatusCacheSeconds` (default 30) — how quickly other app instances notice a disabled workspace
- [x] 15.4 Starter templates for new workspaces (sign-up and super admin create): system setting "Give new workspaces starter templates" (migration), templates built from the demo designs with unique links; tests
- [x] 15.5 Docs + Phase gate

#### Phase 16 — Guide screenshots
- [x] 16.1 `scripts/browser/screenshots.js`: reproducible capture of every guide image from a fresh demo instance
- [x] 16.2 Recapture, review every image, replace; README gallery
- [x] 16.3 Phase gate

#### Phase 17 — .NET 10 LTS
- [x] 17.1 SDK 10.0.300 (installed) in `global.json`, `net10.0`, Microsoft/EF/Npgsql 10.x, other packages checked for .NET 10, `dotnet-ef` 10
- [x] 17.2 Fix breaking changes and new warnings; migrations/model snapshot consistent (`has-pending-model-changes`)
- [x] 17.3 Docker images `10.0`, CI `setup-dotnet` 10, docs/README/plan (tech stack, prerequisites)
- [x] 17.4 Verification: all tests, `smoke.sh` (all roles), browser check, PostgreSQL via the live compose stack, container smoke
- [x] 17.5 Phase gate + CHANGELOG (2.1.0)

### 11.3 Log

| Date | Phase / task | Notes |
|---|---|---|
| 2026-09-28 | Plan | Branch `feature/v2-follow-ups` from `main` (`f6fdc38`, PR #2 merged). .NET SDK 10.0.300 is already installed. Next: 15.1 |
| 2026-09-28 | **Phase 15 done** | 15.1: scratch DB `smartsurvey_upgrade_test` dropped; pre-upgrade dump copied (byte-identical, `pg_restore --list` OK) to `../backups/` with a README. 15.2: `App:PublicBaseUrl` → `PublicUrls` (origin rewrite; e-mail sender rewrites every link, share/embed/join/slug preview use it; invalid value fails start-up; tests incl. HTML-encoded links and path base). 15.3: `Workspaces:StatusCacheSeconds` (0–3600, 0 = no cache). 15.4: `StarterTemplates` (Customer satisfaction, Team pulse check, Event feedback from the demo designs, `-template-xxxxxx` slugs) on sign-up and super admin create, `PlatformSettings.ProvideStarterTemplates` + migration `AddStarterTemplatesSetting` (default true for existing rows), System → Settings toggle. 858 unit + 76 integration green; containers rebuilt (migration applied live), smoke OK, live check: new workspace gets 3 templates (cleaned up). Next: 16.1 |
| 2026-09-28 | **Phase 16 done** | `scripts/browser/screenshots.js` (`npm run screenshots`): all 19 guide/README images from a fresh demo instance (API prep: pending workspace; workspace settings; public address `https://surveys.example.com` via `App:PublicBaseUrl`; waits for circuits; phone 390×844@2×). Every image reviewed; display-logic caption fixed to match its picture (the old one showed a different rule). 858 unit + 76 integration green; containers rebuilt, smoke OK. Next: 17.1 (.NET 10) |
| 2026-09-28 | **Phase 17 done → 2.1.0** | SDK 10.0.300, `net10.0`, C# 14, packages 10.0.12 / Npgsql 10.0.3, `dotnet-ef` 10, images `10.0`; BL0008 (15 account pages: `= default!` + `Input ??= new()`) and ASPDEPR005 (`KnownIPNetworks`) fixed, build 0 warnings; `has-pending-model-changes` clean. .NET 10 adds a Blazor CSP `frame-ancestors` header → switched off (our middleware is the single source), WebSocket compression off while embedding is enabled; verified in a cross-origin iframe (circuit interactive). Npgsql 9+ GSS encryption logged `libgssapi_krb5.so.2` in the container → `PostgresConnectionString.WithDefaults` disables it unless configured. Verification: 862 unit + 76 integration green, `smoke.sh` all roles, browser check 17/17 without browser errors, fresh PostgreSQL (all migrations + full container smoke incl. PDF export on the Ubuntu 24.04 image), live stack rebuilt (no pending migrations, smoke OK), no vulnerable packages. CHANGELOG 2.1.0, `<Version>` 2.1.0. **Follow-ups complete** |
| 2026-09-28 | Post-phase fix | The container served no `_framework/blazor.web.js` (404 → no button worked, e.g. Preview): .NET 10 adds Blazor's script package only when restore sees `.razor` files, and the Dockerfile restores with the project files alone → `RequiresAspNetWebAssets=true` in `SmartSurvey.Web.csproj`. Missed because container checks were HTTP-only and browser checks ran on Development builds → `container-smoke.sh` checks every script the pages load; new `scripts/browser/buttons.check.js` (key buttons + crawl of every button per role) runs against a throwaway container of the real image |
| 2026-09-28 | Post-phase fix | Switching accounts: logging out returned to the page (e.g. `/admin/surveys`) → sign-in page with that `ReturnUrl` → the next account landed on "Access denied". `PostLogoutPath`: back only to pages anyone can open, otherwise home (8 integration cases). GUI check: clicks close open menus first, skip covered elements, refuse to run unless the demo admin password works; 105/105 on the fixed image |
| 2026-09-28 | Account settings | User request "improve the profile page gui": `ManageLayout` (account card + settings menu, section highlighted on sub-pages, wraps on phones), `SettingsCard`, profile with display name (claim refreshed → top bar) and account overview, all 12 account pages restyled (labels linked to fields, hints, clear messages), navbar labels no longer wrap. Found and fixed on the way: empty optional phone rejected by `[Phone]`; authenticator setup without QR code and with issuer "Microsoft.AspNetCore.Identity.UI" (QRCoder SVG, issuer = product name). GUI check: account scenarios (profile, password, full 2FA with a computed TOTP code) |
| 2026-09-28 | Docs refresh | User request "make sure all documents and guides are up to date": README (accounts & security, starter templates, browser checks), DOCUMENTATION (feature list, § 9.4 starter templates, § 15.4 account settings, design system, testing incl. the SQLite pitfall, dotnet-ef 10.0.12), in-app Guide (account settings screenshot `account-settings.webp` via `screenshots.js`, Log out step, labels "Log in"/"Forgot password?"), FAQ ("Your account" group, who can see answers in v2 terms, starter templates), CHANGELOG, CLAUDE.md (GUI check on a container for UI changes). Found on the container run: the profile page rendered before its data loaded (NullReferenceException on PostgreSQL only) → sections render once the account is loaded |
| 2026-09-28 | CI fix | "Apply migrations to PostgreSQL" failed with NETSDK1004 (no `project.assets.json`): the job ran `dotnet ef` right after `dotnet tool restore`, and dotnet-ef 10 reads the project metadata through MSBuild, which needs the restored assets. The job now builds the web project first and runs both ef commands with `--no-build`. All three jobs reproduced in a fresh clone: migrations (no model changes, 6 migrations applied), build & test (Release, 954 tests, no vulnerable packages), Docker (19/19 container smoke incl. PDF and Blazor script) |
