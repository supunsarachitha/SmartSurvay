# SmartSurvey — Technical & User Documentation

> Version 1.0 · ASP.NET Core 8 · Blazor (Interactive Server) · EF Core 8 · PostgreSQL 16
>
> This is the single reference document for SmartSurvey: what it does, how it is built, how to run,
> configure, extend, test and deploy it. The development history lives in
> [`CHANGELOG.md`](../CHANGELOG.md) and the build plan in [`DEVELOPMENT_PLAN.md`](../DEVELOPMENT_PLAN.md).

---

## Table of contents

1. [Overview](#1-overview)
2. [Feature list](#2-feature-list)
3. [Technology stack](#3-technology-stack)
4. [Getting started](#4-getting-started)
5. [Architecture](#5-architecture)
6. [Project structure](#6-project-structure)
7. [Data model & database schema](#7-data-model--database-schema)
8. [Database migrations](#8-database-migrations)
9. [Designing surveys](#9-designing-surveys)
10. [Conditional logic](#10-conditional-logic)
11. [Collecting responses](#11-collecting-responses)
12. [Reporting](#12-reporting)
13. [Exports](#13-exports)
14. [REST API](#14-rest-api)
15. [User interface guide](#15-user-interface-guide)
16. [Security](#16-security)
17. [Configuration reference](#17-configuration-reference)
18. [Testing](#18-testing)
19. [Deployment](#19-deployment)
20. [Extending SmartSurvey](#20-extending-smartsurvey)
21. [Troubleshooting](#21-troubleshooting)
22. [Licensing notes](#22-licensing-notes)

---

## 1. Overview

SmartSurvey is a self-hosted, full-stack survey platform that hosts many **workspaces** — fully isolated rooms,
each with its own admins, members, surveys, responses, reports and audit log (§5.3):

* **Workspace admins** design surveys with ten question types, answer options, combined
  *"Other → free text"* options, multi-page layouts and **conditional show/hide logic**; they publish,
  schedule, share (link, QR code, embed) and close surveys, browse individual responses and build
  **dynamic reports** — tables and charts over live data — that can be exported as **PDF, CSV, TXT,
  Excel (XLSX) and JSON**.
* **Respondents** answer surveys in a clean, mobile-friendly runner with live branching, per-page
  validation, a progress bar and **save & resume** drafts. Surveys can be public (anonymous link) or
  require an account.
* **Anyone** can create a workspace at `/signup` and becomes its admin (a super admin can switch this off or require
  approval); people join a workspace with its join link.
* **Super admins** run the system in the **System console** (`/system`): create, rename, approve, disable, enable and
  delete workspaces, manage every account, the branding and the system settings — without seeing workspace content.
* **Integrators** use the same capabilities through a documented **REST API** secured with bearer tokens.

All business rules live in one Application layer that is shared by the Blazor UI and the REST API, so
both behave identically.

## 2. Feature list

| Area | Capabilities |
|---|---|
| Survey design | 10 question types (short text, long text, radio, checkbox, dropdown, number, e-mail, date, star rating, linear scale/NPS); answer options with export values; *"Other (please specify)"* free-text options; required flags; help texts; per-type settings (placeholders, length limits, min/max values, whole numbers, min/max selections, rating size, scale range and labels, date range, randomised options); multi-page sections; question codes for exports |
| Logic | Show/hide rules for questions **and** sections; conditions on earlier answers with 10 operators; All/Any matching; chained logic; server-side re-evaluation |
| Lifecycle | Draft → Published → Closed → Archived; reopen; schedule (opens/closes at); response quota; templates; duplicate; JSON import/export of survey definitions; optimistic concurrency |
| Distribution | Public link `/s/{slug}`, QR code, invitation text, embedding in other websites (`/embed/s/{slug}` + snippet), anonymous or login-required surveys, one-response-per-user option |
| Responding | Multi-page runner, progress bar, live branching, per-page and final validation (errors clear as answers are fixed), per-respondent option shuffling, drafts with auto-save and resume for logged-in users, thank-you page, "My responses", admin preview (desktop/phone) |
| Responses | Filterable, paged response browser; response detail; deletion; raw export (CSV/XLSX/JSON) |
| Reports | Saved report definitions per survey; global filters (date range, drafts, answer-based filters with All/Any); 10 widget types (KPIs, question tables, bar/horizontal bar/pie/doughnut charts, responses-over-time line chart, cross-tabulation, text responses, raw grid); numeric statistics incl. NPS; auto-generated default report; live preview builder |
| Exports | PDF (QuestPDF, charts embedded as SVG), CSV (RFC 4180, Excel-friendly, CSV-injection safe), TXT (ASCII tables + text bar charts), XLSX (ClosedXML, one sheet per widget), JSON |
| Workspaces | Isolated tenants with their own admins and members; self-service sign-up (optional approval); join link; public workspace page `/w/{slug}` with its public surveys; workspace settings (name, description, contact, join link, public page) |
| Administration | Per workspace: dashboard (KPIs, 30-day trend, top surveys, recent responses), member management (create, roles, set password, lock/unlock, delete with self- and last-admin protection), audit log, settings |
| System console | Super admins: overview across workspaces (figures only), workspaces (create with first admin, rename/re-address, approve, disable with a reason, enable, delete), accounts of every workspace and super admins, branding (product name, tagline, icon or logo/favicon), system settings (sign-up, approval, support e-mail), system audit log |
| Platform | ASP.NET Core Identity (cookie + bearer tokens, lockout, 2FA pages), account e-mails over SMTP, role policies (SuperAdmin, workspace Admin, User), ProblemDetails errors, rate limiting, security headers, **encryption at rest** of respondents' written answers, password-protected surveys, bot/spam protection, health checks, Swagger/OpenAPI, dark mode, responsive UI, Docker, CI |
| Extras | FAQ page, Buy Me a Coffee support page with QR code |

## 3. Technology stack

| Concern | Technology |
|---|---|
| Runtime | .NET 8 (LTS) / ASP.NET Core 8 — `net8.0`, SDK pinned in `global.json` |
| UI | Blazor Web App: static SSR by default, **Interactive Server** for rich pages |
| Styling | Bootstrap 5.3.8, Bootstrap Icons 1.13.1, Inter variable font — all self-hosted in `wwwroot/lib` |
| ORM | Entity Framework Core 8.0.31 |
| Database | PostgreSQL 16 via Npgsql 8.0.11 (primary); SQLite (demos & tests) |
| Identity | ASP.NET Core Identity with GUID keys; cookie auth (UI) + Identity bearer tokens (API) |
| Validation | FluentValidation 12 |
| PDF | QuestPDF 2026.9 (Community license) |
| Excel | ClosedXML 0.105 |
| QR codes | QRCoder 1.8 |
| E-mail | MailKit 4.18 (SMTP) |
| API docs | Swashbuckle 10 (OpenAPI) |
| Tests | xUnit 2.9, bUnit 2.11, `WebApplicationFactory` (DI scope validation on), SQLite in-memory |
| DevOps | Dockerfile, docker-compose, GitHub Actions |

> ⚠️ **.NET 8 support ends on 10 November 2026.** The target framework is defined once in
> `Directory.Build.props` (and the SDK in `global.json`), so moving to .NET 10 LTS is a small change:
> update both, bump the `8.0.*` Microsoft packages in `Directory.Packages.props` to `10.0.*`, rebuild and
> run the tests.

## 4. Getting started

### 4.1 Prerequisites

* .NET 8 SDK (8.0.4xx) — <https://dotnet.microsoft.com/download>, `winget install Microsoft.DotNet.SDK.8` (Windows),
  `brew install --cask dotnet-sdk@8` (macOS) or your Linux distribution's `dotnet-sdk-8.0` package
* PostgreSQL 16 — native install **or** Docker (easiest: `docker compose up -d db`)
* (optional) Docker Desktop / Docker Engine for the container stack

### 4.2 Database

**Native PostgreSQL** (e.g. `winget install PostgreSQL.PostgreSQL.16`, `brew install postgresql@16` or
`apt install postgresql-16`), then create the role and database:

```sql
CREATE ROLE smartsurvey LOGIN PASSWORD 'smartsurvey' CREATEDB;
CREATE DATABASE smartsurvey OWNER smartsurvey;
```

**Docker**: `docker compose up -d db` starts PostgreSQL 16 with the same credentials
(set `POSTGRES_PORT` if 5432 is taken).

### 4.3 Run

```bash
dotnet tool restore                      # installs the local dotnet-ef 8.0.31 tool
dotnet run --project src/SmartSurvey.Web # https://localhost:7047 · http://localhost:5057
```

On start-up the app applies pending migrations and seeds roles, an administrator and — in Development —
demo data (surveys with logic, ~200 responses and a sample report).

| Account (Development) | Password | Role |
|---|---|---|
| `admin@smartsurvey.local` | `Admin123!` | Admin |
| `user@smartsurvey.local` | `User123!` | User |

Swagger UI: `/swagger` · Health: `/health`.

### 4.4 Zero-install demo mode (SQLite)

```bash
# bash / zsh
Database__Provider=Sqlite ConnectionStrings__DefaultConnection="Data Source=smartsurvey.db" \
  dotnet run --project src/SmartSurvey.Web
```

```powershell
# PowerShell
$env:Database__Provider="Sqlite"; $env:ConnectionStrings__DefaultConnection="Data Source=smartsurvey.db"
dotnet run --project src/SmartSurvey.Web
```

SQLite mode creates the schema with `EnsureCreated` (no migrations) and is intended for demos and tests.

### 4.5 Full container stack

```bash
cp .env.example .env             # optional: passwords, ports, demo data, e-mail (SMTP_*) …
docker compose up -d --build     # http://localhost:8080  (admin password: ChangeMe123! unless ADMIN_PASSWORD is set)
scripts/container-smoke.sh       # health, pages, admin sign-in and a PDF export against the running stack
```

Step-by-step instructions for non-developers — installing Docker, everyday commands (stop/start/update/logs), backup
and restore, the `.env` settings and troubleshooting — are in the README section
[**Run with Docker**](../README.md#-run-with-docker-recommended). The compose file maps the `.env` variables to the
application settings of [§17](#17-configuration-reference) (e.g. `SMTP_HOST` → `Email:Smtp:Host`).

## 5. Architecture

SmartSurvey follows **Clean Architecture**. Dependencies point inwards; the Web project is the
composition root.

```mermaid
flowchart LR
    subgraph Web["SmartSurvey.Web (ASP.NET Core host)"]
        UI["Blazor components<br/>(SSR + Interactive Server)"]
        API["Minimal API endpoints<br/>/api/v1/*"]
        ID["Identity UI + /api/auth"]
    end
    subgraph App["SmartSurvey.Application"]
        SVC["Services<br/>Survey · Response · Report · Dashboard · Audit · Branding"]
        LOGIC["LogicEvaluator · ResponseValidator"]
        ENG["ReportEngine · SvgChartRenderer"]
        CONTRACTS["DTOs · interfaces · validators"]
    end
    subgraph Infra["SmartSurvey.Infrastructure"]
        DB["AppDbContext (EF Core)<br/>migrations · interceptor"]
        EXP["Exporters<br/>PDF · CSV · TXT · XLSX · JSON"]
        USR["UserAdminService · DbSeeder"]
        MAIL["SmtpEmailTransport (MailKit)"]
    end
    subgraph Domain["SmartSurvey.Domain"]
        ENT["Entities · enums · value objects"]
    end
    UI --> SVC
    API --> SVC
    SVC --> CONTRACTS
    SVC --> LOGIC
    SVC --> ENG
    Infra --> App
    App --> Domain
    DB --> PG[(PostgreSQL / SQLite)]
```

### 5.1 Key design decisions

| Decision | Rationale |
|---|---|
| **One Application layer for UI and API** | Blazor Server components call services in-process; REST endpoints are thin wrappers over the same services — no duplicated rules. |
| **Shared logic & validation code** | `LogicEvaluator` and `ResponseValidator` are pure functions over DTOs. The runner uses them for live branching/validation; `ResponseService` re-runs them on submission (the client is never trusted). |
| **`IAppDbContextFactory`** | Blazor Server DI scopes live as long as a user's circuit; services therefore create a short-lived `DbContext` per operation. |
| **Client-generated GUID keys** | Complete survey graphs (questions referenced by logic rules) can be built in memory — in the builder or by API clients — before the first save. Keys are `ValueGeneratedNever()` so reconciliation marks new entities as *Added*. |
| **UTC `DateTime`, `double` numbers, string enums, JSON settings** | Portable across PostgreSQL and SQLite; settings columns are `jsonb` on PostgreSQL. |
| **Server-side SVG charts** | One renderer produces the charts for the browser *and* for PDFs — identical output, no JavaScript chart library, fully unit-testable, XSS-safe. |
| **Pluggable exporters** | `IReportExporter` implementations are discovered through DI; adding a format is one class + one registration. |
| **Cookie-or-bearer policy scheme** | The default authentication scheme forwards to the bearer handler when an `Authorization: Bearer` header is present, otherwise to the Identity cookie, so `ICurrentUser` works for both UI and API callers. |

### 5.2 Request flows

* **Interactive page** (e.g. survey builder): browser ⇄ SignalR circuit ⇄ Blazor component → service →
  `IAppDbContextFactory` → EF Core → PostgreSQL. `ICurrentUser` is populated by a `CircuitHandler`.
* **Static page** (e.g. FAQ): HTTP request → Razor component rendered once on the server (SSR).
* **REST call**: HTTP request → authentication (cookie or bearer) → `CurrentUserMiddleware` → endpoint →
  service; expected failures become RFC 7807 ProblemDetails in `ApiErrorFilter` (endpoint filter on `/api/v1`),
  anything unexpected in `ApiExceptionHandler` (logged, details hidden outside Development).
* **Survey runner**: the page is prerendered (the session and the option-shuffle seed are handed to the interactive
  render through persisted component state), then answers are edited in `SurveyRunState`, which runs the same
  `LogicEvaluator` / `ResponseValidator` as the server.

### 5.3 Workspaces (multi-tenancy)

A **workspace** is a tenant: an isolated room with its own admins, members, surveys, responses, reports and audit log.
Nothing is shared between workspaces. The code and the UI both call it *workspace*.

**Roles**

| Role | Belongs to | Can |
|---|---|---|
| `SuperAdmin` | no workspace | System console `/system`, `/api/v1/system/*`: workspaces, accounts of every workspace, branding, system settings, system audit log. **Cannot** open surveys, responses, reports or a workspace's audit log. |
| `Admin` | one workspace | Everything inside that workspace: surveys, responses, reports, members (roles Admin/User), audit log, workspace settings. Cannot change branding. |
| `User` | one workspace | Answer the workspace's surveys (incl. members-only ones), drafts, "My responses". |

**Rules**

* **One account = one workspace.** E-mail addresses stay unique system-wide because sign-in is global; a person who
  works in two workspaces uses two addresses. Accounts never move between workspaces.
* **Survey links stay `/s/{slug}`**; slugs are unique system-wide so existing links, QR codes and embeds keep working.
  A share link works for everyone the survey allows. Members of *another* workspace answer anonymous surveys as guests
  (no link to their account, no drafts) and are refused on members-only surveys ("this survey is for another
  workspace").
* **Disabled / pending workspaces:** members cannot sign in (the login page explains why, but only after the password
  was verified), existing cookies and API tokens are refused at once (`WorkspaceAccessMiddleware`: pages sign out and
  show `/workspace-unavailable`, API and `/_blazor` get 403), open Blazor circuits are signed out within a minute, and the
  workspace's survey links and page show "not available". Data is kept; deleting is only possible for a disabled
  workspace and requires typing its name.
* **Sign-up:** `/signup` creates a workspace plus its first admin. `PlatformSettings.AllowWorkspaceSignup` switches it
  off; `RequireWorkspaceApproval` makes new workspaces wait (`PendingApproval`) until a super admin approves them.

**How isolation is enforced** (fail-closed, in the data layer — services cannot forget it)

1. Every tenant-owned table has a `WorkspaceId` (`IWorkspaceOwned`: surveys and their sections, questions, options and
   logic, responses, answers, selections, reports and widgets); `AuditLogs.WorkspaceId` is nullable (null = system
   event); `AspNetUsers.WorkspaceId` is null only for super admins.
2. Every `AppDbContext` has a `DataScope`: `None` (the default — sees no workspace data), `Workspace(id)` or `System`
   (unfiltered, only for system code). Global query filters limit every tenant table and the audit log to the scope.
3. `SaveChanges` stamps `WorkspaceId` on inserts and throws on writes that would touch another workspace or change the
   workspace of existing rows.
4. `IAppDbContextFactory.CreateAsync()` scopes to the current user's workspace (from the `workspace_id` claim, which is
   fixed for an account) and refuses members of inactive workspaces; `CreateForWorkspaceAsync(id)` is used by the
   respondent flow after resolving a share link; `CreateSystemAsync()` only by seeding/start-up, super admin services,
   the system-wide slug check and share-link resolution (which read nothing but ids, status and public fields).
5. Accounts (`AspNetUsers`) are limited to the workspace inside a workspace scope, so e.g. the dashboard's member
   count or respondent names can never include other workspaces; unscoped contexts keep global look-ups for Identity
   (sign-in, unique e-mail checks). `Workspaces` is not filtered. Services additionally check roles (workspace admin vs.
   super admin).

The workspace status, name and slug are cached per app instance (`WorkspaceStatusCache`, `Workspaces:StatusCacheSeconds`,
default 30) and dropped immediately when this instance changes them; with several app instances the others pick a change
up within that time — lower it (or `0` = no cache) when disabling must take effect everywhere at once.

**Starter templates:** unless switched off in System → Settings (`PlatformSettings.ProvideStarterTemplates`), every new
workspace — self-service or created by a super admin — starts with three draft templates (customer satisfaction with
display logic, a members-only team pulse check, event feedback), so "create from template" works on day one.

## 6. Project structure

```
SmartSurvey.sln
├── src/
│   ├── SmartSurvey.Domain/            Entities (Survey, SurveySection, Question, QuestionOption, LogicRule,
│   │                                  LogicCondition, SurveyResponse, Answer, AnswerSelection,
│   │                                  ReportDefinition, ReportWidget, AuditLogEntry), enums, value objects
│   │                                  (QuestionSettings, WidgetSettings, ReportFilterSet), ApplicationUser
│   ├── SmartSurvey.Application/       Common (exceptions, paging, abstractions, slugs), Surveys, Responses,
│   │                                  Logic, Reports (+Charts), Exports (contracts), Dashboard, Users, Audit
│   ├── SmartSurvey.Infrastructure/    Persistence (AppDbContext, Configurations, Converters, Migrations,
│   │                                  Seed), Exports (PDF/CSV/TXT/XLSX/JSON, raw responses), Identity
│   └── SmartSurvey.Web/               Program.cs, Api/ (endpoints), Infrastructure/ (current user, auth,
│                                      error handling, security headers/embedding, throttle, browser
│                                      interop), Components/ (Layout, Shared, Pages, Admin/Builder,
│                                      Admin/Reports, Runner, Account), wwwroot/
├── tests/
│   ├── SmartSurvey.UnitTests/         Core logic, services (SQLite in-memory), exporters, bUnit components
│   └── SmartSurvey.IntegrationTests/  REST API end-to-end through WebApplicationFactory
├── docs/                              DOCUMENTATION.md (this file), examples/ (API payloads, .http file)
├── scripts/smoke.sh                   Boots the app on SQLite and smoke-tests pages
├── scripts/container-smoke.sh         Smoke-tests a running container stack (incl. PDF export)
├── Dockerfile · docker-compose.yml · .github/workflows/ci.yml
└── Directory.Build.props · Directory.Packages.props · global.json · DEVELOPMENT_PLAN.md · CHANGELOG.md
```

## 7. Data model & database schema

```mermaid
erDiagram
    Surveys ||--o{ SurveySections : "has pages"
    Surveys ||--o{ Questions : "has"
    SurveySections ||--o{ Questions : "contains"
    Questions ||--o{ QuestionOptions : "has"
    Surveys ||--o{ LogicRules : "has"
    LogicRules ||--o{ LogicConditions : "has"
    LogicRules }o--o| Questions : "targets"
    LogicRules }o--o| SurveySections : "targets"
    LogicConditions }o--|| Questions : "source"
    LogicConditions }o--o| QuestionOptions : "option"
    Surveys ||--o{ Responses : "collects"
    AspNetUsers |o--o{ Responses : "responds"
    Responses ||--o{ Answers : "has"
    Answers }o--|| Questions : "answers"
    Answers ||--o{ AnswerSelections : "selects"
    AnswerSelections }o--|| QuestionOptions : "option"
    Surveys ||--o{ Reports : "analysed by"
    Reports ||--o{ ReportWidgets : "has"
    ReportWidgets }o--o| Questions : "primary/secondary"
    Workspaces ||--o{ Surveys : "owns"
    Workspaces ||--o{ Reports : "owns"
    Workspaces ||--o{ Responses : "owns"
    Workspaces |o--o{ AspNetUsers : "members (null = super admin)"
    Workspaces |o--o{ AuditLogs : "entries (null = system event)"
```

Every table below the workspace (sections, questions, options, logic, answers, selections, widgets) also carries
`WorkspaceId` for filtering (§5.3); only the root tables have a foreign key to `Workspaces`.

| Table | Purpose | Notable columns / constraints |
|---|---|---|
| `Surveys` | Survey aggregate root | `Slug` (unique), `Status` (string enum), `IsTemplate`, schedule `OpensAt`/`ClosesAt`, `MaxResponses`, `Version` (concurrency token), audit columns |
| `SurveySections` | Pages | `Order`; cascade from survey |
| `Questions` | Questions | `Type`, `Code` (unique per survey, service-enforced), `Settings` (**jsonb**), `IsRequired`, `Order` |
| `QuestionOptions` | Choice options | `AllowsFreeText` ("Other → text"), `Value`, `Order` |
| `LogicRules` / `LogicConditions` | Show/hide logic | exactly one of `TargetQuestionId` / `TargetSectionId`; conditions reference `SourceQuestionId`, `Operator`, `OptionId` or `Value` |
| `Responses` | Submissions & drafts | `RespondentId` (nullable, **SET NULL** on user delete), `Status`, `StartedAt`, `SubmittedAt`, `CurrentSectionIndex`, `UserAgent` (**encrypted**) |
| `Answers` | One per question per response | `TextValue` (**encrypted**) / `NumberValue` (double) / `DateValue` (date); unique (`ResponseId`,`QuestionId`) |
| `AnswerSelections` | Selected options | `FreeText` (**encrypted**); unique (`AnswerId`,`OptionId`) |
| `Reports` / `ReportWidgets` | Saved report definitions | `Filters` and `Settings` (**jsonb**); widget question FKs **SET NULL** |
| `Workspaces` | Tenants | `Slug` (unique), `Status` (`Active`, `Disabled`, `PendingApproval`), `StatusReason`, `AllowSelfRegistration`, `ShowPublicSurveyList`, `OwnerId`, audit columns |
| `PlatformSettings` | System settings (single row) | `AllowWorkspaceSignup`, `RequireWorkspaceApproval`, `SupportEmail` |
| `AuditLogs` | Audit trail | `WorkspaceId` (null = system event); indexed by `Timestamp`, (`EntityType`,`EntityId`) and (`WorkspaceId`,`Timestamp`) |
| `BrandingSettings` | Product branding (single row) | `ProductName`, `Tagline`, `IconName`, `LogoContent` (bytes) + content type, `Version` (cache busting) |
| `AspNet*` | ASP.NET Core Identity | GUID keys; `AspNetUsers` adds `WorkspaceId` (null = super admin), `DisplayName`, `CreatedAt`, `LastLoginAt`, `IsActive`; roles `SuperAdmin`, `Admin`, `User` |

Delete behaviour: deleting a survey cascades to its design, responses and reports; deleting a question
cascades to its answers (the builder warns before doing so); deleting a user keeps their responses
(anonymised); deleting a (disabled) workspace removes its responses, reports, surveys, audit entries and accounts in
one transaction (foreign keys to `Workspaces` are restricted, so nothing is removed by accident).

**Encryption at rest:** `Answers.TextValue` (text, paragraph and e-mail answers), `AnswerSelections.FreeText` ("Other"
texts) and `Responses.UserAgent` are encrypted with ASP.NET Core Data Protection (AES-256-CBC + HMAC-SHA256,
randomised, automatic key rotation) through EF Core value converters (`Persistence/Encryption/FieldEncryption.cs`).
Stored values look like `enc:v1:…`; the columns are `text` because ciphertext is longer than the answer limits, which the
application enforces. The keys live in the data-protection key ring (`DataProtection:KeysPath`; the `keys` volume in
Docker) — **not** in the database — so a database dump or backup alone doesn't reveal what respondents wrote. Choices,
numbers and dates stay plain because reports aggregate them in the database; survey designs, user accounts (Identity:
e-mail addresses are needed for sign-in and search; passwords are PBKDF2 hashes) and the audit log are not encrypted.
Values written before encryption was enabled stay readable and are encrypted once at start-up (`FieldEncryptionMigrator`).
A value whose key is missing is shown as a placeholder instead of breaking pages. `Encryption:Enabled=false` stops
encrypting new values (existing ones stay readable).

Conventions (see `AppDbContext`): all `DateTime` values are stored/read as UTC; enums are stored as
strings (max 40); settings objects use a JSON value converter with value comparers so in-place edits are
detected; the auditable-entity interceptor stamps `CreatedAt/By` and `UpdatedAt/By`.

## 8. Database migrations

Migrations target PostgreSQL and live in `src/SmartSurvey.Infrastructure/Persistence/Migrations`.
A design-time factory (`DesignTimeDbContextFactory`) lets `dotnet ef` work without starting the web host.

```bash
dotnet tool restore
# add a migration after changing the model
dotnet ef migrations add <Name> --project src/SmartSurvey.Infrastructure --startup-project src/SmartSurvey.Web --output-dir Persistence/Migrations
# apply to the database in SMARTSURVEY_MIGRATIONS_CONNECTION (default: localhost smartsurvey/smartsurvey)
dotnet ef database update --project src/SmartSurvey.Infrastructure --startup-project src/SmartSurvey.Web
# generate an idempotent SQL script for DBAs
dotnet ef migrations script --idempotent --project src/SmartSurvey.Infrastructure --startup-project src/SmartSurvey.Web -o migrate.sql
```

The web app applies pending migrations at start-up when `Database:ApplyMigrationsOnStartup` is `true`
(default). Disable it in environments where schema changes are deployed separately.

**Upgrading from 1.x (`AddWorkspaces`):** the migration creates the `Workspaces` table and — only when the database
already contains accounts, surveys or audit entries — a workspace **"Default workspace"** (slug `default`) that receives
all existing data and accounts; existing admins stay its admins. Survey links do not change. At the next start the seeder
creates the first super admin from `Seed:SuperAdminEmail` / `Seed:SuperAdminPassword` (Docker: `SUPERADMIN_EMAIL`,
`SUPERADMIN_PASSWORD`). Back up the database (and the data-protection keys) before upgrading.

## 9. Designing surveys

### 9.1 Question types

| Type | Stored in | Settings |
|---|---|---|
| Short text | `TextValue` | placeholder, min/max length |
| Long text (paragraph) | `TextValue` | placeholder, min/max length |
| Single choice (radio) | `AnswerSelections` (1) | options, randomise |
| Multiple choice (checkbox) | `AnswerSelections` (n) | options, min/max selections, randomise |
| Dropdown | `AnswerSelections` (1) | options, randomise |
| Number | `NumberValue` | min/max value, allow decimals, placeholder |
| E-mail | `TextValue` | format validation |
| Date | `DateValue` | min/max date |
| Star rating | `NumberValue` | number of stars (2–10) |
| Linear scale / NPS | `NumberValue` | min/max (e.g. 0–10), end labels |

**Combined "Other → free text" options:** any option of a choice question can set *Allows free text*.
When a respondent selects it a text box appears; the text is stored on the selection and is required
while the option is selected. Reports list these texts separately.

### 9.2 Sections

Sections are pages. The runner shows one visible section per page; sections can be hidden by logic, and a page
whose questions are all hidden for the current answers is skipped (a page without any questions is shown as an
information page).

### 9.3 Lifecycle

| From → To | Rule |
|---|---|
| Draft → Published | at least one question; templates cannot be published |
| Published → Closed | stops accepting responses |
| Closed → Published | reopen |
| any → Archived | hidden from default lists |
| Archived → Draft | restore |

A published survey accepts responses only inside its schedule (`OpensAt` ≤ now < `ClosesAt`) and while the
completed-response quota (`MaxResponses`) is not reached.

### 9.4 Templates, duplication, import/export

* *Duplicate* deep-copies the design with new ids (all logic references remapped); responses are not copied.
* *Templates* appear in the "start from template" gallery.
* *Export* produces a portable `SurveyExportDocument` (JSON, `schemaVersion: 1`); *Import* creates a new
  draft with fresh ids.

## 10. Conditional logic

A **rule** targets one question or section, has an action (**Show** or **Hide**), a match type
(**All** = AND, **Any** = OR) and one or more **conditions** on *earlier* questions.

Evaluation (`LogicEvaluator`), in display order:

1. A target without rules is visible.
2. If it has **Show** rules it is visible only when at least one Show rule matches.
3. If any **Hide** rule matches it is hidden (Hide wins).
4. Answers to hidden questions count as *unanswered* for later conditions (chained logic), and a hidden
   section hides all its questions.

| Operator | Choice questions | Text | Number / rating / scale | Date |
|---|---|---|---|---|
| Equals / Not equals | option (not) selected | case-insensitive equality | numeric equality | same day |
| Contains / Not contains | option (not) selected | substring (case-insensitive) | — | — |
| >, ≥, <, ≤ | — | — | numeric comparison | date comparison |
| Is answered / Is not answered | any selection | non-blank | has value | has value |

Negative operators are true for unanswered questions ("the answer is not X"). Numbers use the invariant
culture (`3.5`), dates ISO 8601 (`2026-03-01`).

**Example** — show *"What went wrong?"* when satisfaction is *Dissatisfied* or *Very dissatisfied*:

```json
{
  "targetQuestionId": "…what-went-wrong…",
  "action": "Show",
  "matchType": "Any",
  "conditions": [
    { "sourceQuestionId": "…satisfaction…", "operator": "Equals", "optionId": "…dissatisfied…" },
    { "sourceQuestionId": "…satisfaction…", "operator": "Equals", "optionId": "…very-dissatisfied…" }
  ]
}
```

The designer validator rejects rules that reference later questions, unknown options, unsupported
operators or unparsable values.

## 11. Collecting responses

**Eligibility** (checked when starting and again when submitting): survey exists → published and inside
its schedule → quota not reached → login present when required → not already responded (unless multiple
responses are allowed).

**Drafts:** logged-in respondents' answers are saved when they move between pages, when they leave the
survey and when they click *Save & finish later*; opening the survey again restores the answers and page
("Welcome back", with an option to start over). Guests are warned before leaving a survey with unsent answers.

**Password protection:** a survey can require a password (*Settings → Require a password to open the survey*). Only a
salted PBKDF2 hash (`Surveys.AccessPasswordHash`) is stored. Respondents enter the password once
(`IResponseService.UnlockAsync`, `POST /api/v1/public/surveys/{slug}/unlock`) and receive a signed access key (ASP.NET
Core Data Protection, valid 12 hours, tied to the survey and its current password — changing the password invalidates
old keys). The key must accompany opening (`X-Survey-Access-Key` header / runner state), saving drafts and submitting
(`accessKey`); without it the session reports `PasswordRequired` and the design is withheld, and saves/submissions are
refused (403). Wrong guesses are limited per connection (10 per 5 minutes) and per IP on the API. Protected surveys are
not listed publicly and show their thank-you message inside the survey page. Duplicates keep the password; exported
definitions never contain it.

**Bot and spam protection** (anonymous submissions; signed-in respondents are accountable and not challenged):

1. **Proof of work** — the session (`StartOrResumeAsync`, `GET /api/v1/public/surveys/{slug}`) contains a `challenge`
   (ALTCHA-style: `salt`, `challenge`, `maxNumber`, `signature`). The browser searches the number `n`
   (0 ≤ n ≤ maxNumber) with `sha256(salt + n)` = `challenge` (lower-case hex) in a Web Worker (`wwwroot/js/pow-worker.js`)
   while the person answers, and sends `{ salt, challenge, signature, number }` as `challenge` with the submission.
   ~25,000 hashes on average (`BotProtection:Difficulty` 50,000): a fraction of a second for a person, a real cost at spam
   scale. The signature (Data Protection) makes challenges unforgeable; the salt contains the survey and issue time.
2. **Minimum time** — submissions less than `BotProtection:MinimumSeconds` (3) after the challenge was issued are rejected.
3. **Honeypot** — a visually hidden `website` field; submissions that fill it are rejected.
4. **One-time use** — a challenge is consumed by a successful submission (replays are rejected; validation errors don't
   consume it). Challenges expire after `BotProtection:ChallengeLifetimeHours` (24).
5. **Rate limits** — per IP on the API (30 submissions/min) and per connection in the runner (5/min).

Rejected submissions get a friendly 422 message ("please reload the page…"). API integrators submitting anonymously
must solve the challenge (a loop over SHA-256, see `tests/…/ApiTestData.Solve`) or submit with a bearer token.

**Option order:** choice questions with *Randomise options* are shuffled once per respondent (the order stays
the same while they answer); "Other → free text" options always stay last.

**Embedding:** `/embed/s/{slug}` shows the runner without site navigation so it can be placed in an
`<iframe>` on another website (snippet on the share page). Browsers don't send sign-in cookies to
third-party frames, so embedded respondents answer as guests — members-only surveys show a button that
opens the full page in a new tab. See `Embedding:*` in the [configuration reference](#17-configuration-reference).

**Submission:** answers are sanitised (unknown questions/options dropped, text trimmed and length-capped),
logic is re-evaluated, answers to hidden questions are discarded, all visible questions are validated
(required, formats, ranges, selections, free-text for "Other"), then the response is stored as
*Completed*. Validation errors are returned per question id.

## 12. Reporting

A **report** belongs to one survey and contains **filters** and an ordered list of **widgets**. Reports are
computed on demand by `ReportEngine`; only the definition is stored.

**Filters:** date range (inclusive, UTC, on submission/start time), include drafts, and answer filters that
use the same operators as logic conditions (combined with All/Any).

| Widget | Output |
|---|---|
| Summary statistics | KPIs: responses, completed, in progress, completion rate, average time, first/last response |
| Question table | Distribution (count, % of respondents) for choice questions; statistics + distribution for numeric; monthly distribution for dates; latest answers for text; "Other" free-text table |
| Bar / Horizontal bar / Pie / Doughnut chart | Chart of the distribution (+ optional data table) |
| Responses over time (line) | Responses per day / week / month, zero-filled |
| Cross-tabulation | Counts for every option pair of two choice questions with totals (+ stacked bar chart) |
| Text responses | Latest free-text answers |
| Raw responses | One row per response, one column per selected question |

Numeric statistics include count, mean, median, min, max, standard deviation and — for 0–10 scales —
the **Net Promoter Score** (% promoters 9–10 minus % detractors 0–6). Widgets whose question was deleted
show an explanatory error instead of failing the whole report. *Build default report* creates a sensible
starting report for any survey.

## 13. Exports

| Format | Generator | Contents |
|---|---|---|
| PDF | QuestPDF | A4 report with header/footer, page numbers, filter summary, KPI grids, embedded SVG charts, styled tables |
| CSV | built-in RFC 4180 writer | UTF-8 with BOM (opens correctly in Excel); one block per widget; **CSV-injection protection** (text cells beginning with `= + - @` are prefixed with `'`) |
| TXT | built-in | Human-readable ASCII tables and text bar charts |
| XLSX | ClosedXML | Summary sheet + one worksheet per widget with typed numeric cells |
| JSON | System.Text.Json | The complete report result model |

Raw response exports (CSV, XLSX, JSON) contain one row per response with one column per question.

## 14. REST API

Base path `/api/v1`. Authentication: either the Identity cookie (same-site browser calls) or a
**bearer token** from the Identity API:

```http
POST /api/auth/login
Content-Type: application/json

{ "email": "admin@smartsurvey.local", "password": "Admin123!" }
```

The response contains `accessToken` (and `refreshToken`); send `Authorization: Bearer <accessToken>`.
Tokens are refreshed with `POST /api/auth/refresh`. Enums are serialised as strings; timestamps are UTC
ISO 8601.

**Errors** are RFC 7807 ProblemDetails: `400` validation (with an `errors` dictionary — for survey answers
keyed by question id), `401` unauthenticated, `403` forbidden, `404` not found, `409` conflict
(stale `version`, duplicate slug), `422` business rule (e.g. survey closed), `429` rate limited.

**Workspaces:** every call runs in the caller's workspace — ids of other workspaces' surveys, responses and reports
are simply `404`. Workspace admins use the groups `/surveys`, `/responses`, `/reports`, `/dashboard`, `/users`, `/audit`
and `/workspace`; super admins use `/system/*` (overview, workspaces, accounts, settings, audit) and `/branding`, and get
`403` on workspace content. Accounts are created per workspace: `POST /api/v1/public/workspaces` (sign-up) and
`POST /api/v1/public/workspaces/{slug}/register` (join); the Identity API's `POST /api/auth/register` returns `403`.
`GET /api/v1/public/surveys` lists the caller's workspace, or with `?workspace={slug}` a workspace's public surveys.

The full endpoint list is in [DEVELOPMENT_PLAN.md §6 and §10.3](../DEVELOPMENT_PLAN.md#6-routes) and interactively
in Swagger (`/swagger`). Ready-to-run examples: [`docs/examples`](examples/) (steps 16–23 cover workspaces and the
System endpoints; a test checks that every request in the walkthrough is a real API operation).

## 15. User interface guide

The app includes a **user guide** for non-technical users at `/guide` (linked in the top menu, the footer, the FAQ and
the admin sidebar): plain-language, step-by-step chapters with screenshots for respondents and administrators. The
screenshots live in `wwwroot/img/guide` (WebP) and are also used by the README.

### 15.1 Respondents

1. Open a shared link `/s/{slug}` (or an embedded survey on another site), a workspace's page `/w/{slug}`, or — as a
   member — **Surveys** (`/surveys`, your workspace's open surveys). Join a workspace with its join link
   (`/Account/Register?workspace={slug}`).
2. Answer page by page; questions appear or disappear based on earlier answers; required questions are
   marked with `*`; the progress bar shows how much is answered. Problems are shown next to the question
   when moving on, and disappear as soon as the answer is fixed.
3. Logged-in users' progress is saved automatically on every page change, or with *Save & finish later*;
   drafts appear in **My responses** (*Continue*).
4. After submitting, the thank-you page (`/s/{slug}/thank-you`) shows the survey's thank-you message and,
   when the survey allows it, a button to answer again.

### 15.2 Administrators

1. **Dashboard** (`/admin`) — KPIs, 30-day trend, top surveys, recent responses.
2. **Surveys** (`/admin/surveys`) — create (blank or from template), import JSON, filter by status,
   duplicate, export, publish/close/archive.
3. **Builder** (`/admin/surveys/{id}/edit`) — *Questions* (sections, question editors, options, settings),
   *Logic* (rule wizard with plain-language preview), *Settings* (slug, messages, access, schedule,
   quota). Unsaved changes are protected.
4. **Preview** (`/admin/surveys/{id}/preview`: the real runner with logic and validation, nothing saved,
   desktop/phone width) and **Share** (link, QR code, invitation text, embed snippet).
5. **Responses** (`/admin/surveys/{id}/responses`) — filter by status, date and respondent, open a response
   (`/admin/responses/{id}`: answers by page, time taken, browser), delete, export raw data (CSV/Excel/JSON,
   optionally including drafts).
6. **Reports** (`/admin/reports`) — *Quick overview* builds a complete report for a survey in one click. In the
   builder (`/admin/reports/new`, `/admin/reports/{id}/edit`) pick the survey, start from the recommended overview or
   small, add widgets (the menu only offers widgets the survey has suitable questions for), choose which responses count
   (date range, drafts, answer filters combined with all/any) and watch the live preview update as you edit. Save, then
   open the viewer (`/admin/reports/{id}`) to refresh, print or export (PDF/Excel/CSV/TXT/JSON).
7. **Users** (`/admin/users`) — the workspace's members: create accounts, grant or revoke the administrator role,
   lock/unlock, delete (you cannot lock, demote or delete yourself, and the workspace's last administrator is
   protected); shows the join link when joining is on. **Audit log** (`/admin/audit`) — who changed or exported what in
   this workspace, with filters. **Workspace settings** (`/admin/settings`) — name, description, contact e-mail, join
   link on/off (with copy button), public survey page on/off. The sidebar shows the workspace's name.

A new workspace starts at **`/signup`** (workspace name, optional address, your account); you land on its dashboard as
its admin — or on a "waiting for approval" page when approval is required.

### 15.3 Super admins (System console)

1. **Overview** (`/system`) — workspaces by status, member accounts, super admins, surveys and responses across the
   site (figures only), workspaces waiting for approval (approve in place), newest workspaces.
2. **Workspaces** (`/system/workspaces`, `/system/workspaces/new`, `/system/workspaces/{id}`) — search and filter;
   create a workspace with its first admin; on a workspace: figures, approve, disable with a message for its members,
   enable, rename or change its address, contact its admins, delete (only when disabled, after typing its name).
3. **Accounts** (`/system/accounts`) — every account with its workspace (filter by workspace and role); create
   super admins or members of a chosen workspace; roles, new password, lock/unlock, delete (the last super admin and a
   workspace's last admin are protected).
4. **Branding** (`/system/branding`) — product name, tagline, icon or logo (also the favicon), with a live preview;
   applies to the whole site. **Settings** (`/system/settings`) — workspace sign-up on/off, approval of new workspaces,
   starter templates for new workspaces on/off, support e-mail. **Audit log** (`/system/audit`) — super admin actions and sign-ups.

### 15.4 Design system

The UI uses a custom design layer on top of Bootstrap 5.3 (`wwwroot/app.css`): indigo primary colour,
slate neutrals, Inter typography, soft shadows, light **and dark themes** (toggle in the top bar,
remembered per browser). Reusable components live in `Components/Shared` (`PageHeader`, `StatusBadge`,
`EmptyState`, `LoadingSpinner`, `StatCard`, `Modal`, `ConfirmDialog`, `Pager`, `LocalDateTime`,
`ChartView`, toasts).

## 16. Security

* **Authentication:** ASP.NET Core Identity, PBKDF2 password hashing, account lockout (5 attempts / 15 min),
  optional e-mail confirmation (`Identity:RequireConfirmedAccount`), 2FA pages included.
* **Authorization:** roles `SuperAdmin`, `Admin` and `User`; policies `Admin` (Admin role **and** a workspace claim) and
  `SuperAdmin` for pages, `ApiAdmin`/`ApiSuperAdmin`/`ApiUser` for the API. Services re-check roles (defense in depth)
  and response ownership.
* **Workspace isolation:** enforced in the data layer (§5.3): fail-closed query filters on every tenant table, a save
  guard against cross-workspace writes, and a context factory that refuses members of inactive workspaces. Ids of other
  workspaces' data are `404`, never `403`, so their existence is not revealed. Members of disabled workspaces are
  refused at sign-in, on every request and in open circuits; the login page only explains why after the password was
  verified. Workspace sign-up and join are rate limited (page and API).
* **API hygiene:** API requests get `401/403` instead of login redirects; ProblemDetails never leak stack
  traces outside Development; expected errors (400/403/404/409/422) are not logged as server errors; rate limiting on
  authentication (20/min/IP) and submissions (30/min/IP). Submissions from the interactive survey runner are limited
  per connection (5 per minute) — not per IP, because many respondents can share one address. For very public
  surveys that attract spam, put a WAF / bot protection in front of the site.
* **Bot and spam protection:** invisible proof-of-work challenge, minimum answering time, honeypot and one-time
  challenges for anonymous submissions, plus rate limits — no CAPTCHA and no third-party service (details in §11).
* **Password-protected surveys:** salted PBKDF2-SHA256 hashes (100,000 iterations), constant-time comparison,
  signed time-limited access keys checked on every open/save/submit, brute-force limits per connection and per IP.
* **Headers:** `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` and
  clickjacking protection (below) on every response.
* **Input handling:** server-side validation of every survey design and answer; logic re-evaluated on the
  server; Blazor HTML-encodes all output; chart SVG text is XML-escaped; CSV exports neutralise formula
  injection.
* **Clickjacking:** every page sends `X-Frame-Options: SAMEORIGIN` and `Content-Security-Policy:
  frame-ancestors 'self'`, except the embeddable `/embed/*` survey pages (any site by default, or only
  `Embedding:AllowedOrigins`; `Embedding:Enabled=false` turns embedding off).
* **Account e-mails:** reset and confirmation links are only sent by e-mail; without SMTP they are never logged outside
  Development, and the "confirm here" shortcut after registering only appears in Development. Delivery errors are logged,
  never shown, so the forms don't reveal whether an address has an account. Administrators can set a new password,
  which signs the user out everywhere.
* **Encryption at rest:** respondents' written answers, "Other" texts and browser details are encrypted in the
  database; the keys are kept outside it (details in §7). For full-disk protection also use encrypted storage/volumes
  for PostgreSQL, and TLS for database connections across networks (`SSL Mode=Require` in the connection string).
* **Data protection:** keys can be persisted (`DataProtection:KeysPath`) so cookies/tokens survive restarts.
* **Transport:** HTTPS redirection and HSTS outside Development; forwarded headers support behind proxies.
* **Host names:** set `App:PublicBaseUrl` (Docker: `PUBLIC_BASE_URL`) so every e-mail and share link carries the site's
  public address whatever `Host` header a request used, and `AllowedHosts` (Docker: `ALLOWED_HOSTS`) to the site's host
  name(s) so the app ignores requests for other hosts.
* **Secrets:** no production credentials in configuration — set `Seed:AdminPassword`, `Seed:SuperAdminPassword` and
  connection strings via environment variables or a secret store.

## 17. Configuration reference

All settings can be provided in `appsettings*.json` or as environment variables (`Section__Key`).

| Key | Default | Description |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | local PostgreSQL | Database connection string |
| `Database:Provider` | `PostgreSQL` | `PostgreSQL` or `Sqlite` |
| `Database:ApplyMigrationsOnStartup` | `true` | Apply pending migrations (PostgreSQL) |
| `Database:InitializeOnStartup` | `true` | Run migrations/seeding at start-up |
| `Seed:CreateAdmin` | `true` | Create the initial admin when none exists |
| `Seed:AdminEmail` | `admin@smartsurvey.local` | Initial admin e-mail |
| `Seed:AdminPassword` | *(empty)* | Initial admin password (required to create the admin) |
| `Seed:WorkspaceName` / `Seed:WorkspaceSlug` | `Default workspace` / `default` | Workspace created for the initial admin and the demo data (an existing workspace with that slug is reused) |
| `Seed:CreateSuperAdmin` | `true` | Create the initial super admin when none exists |
| `Seed:SuperAdminEmail` / `Seed:SuperAdminPassword` | `superadmin@smartsurvey.local` / *(empty)* | Initial super admin (the password is required to create it; an existing account is never promoted) |
| `Seed:DemoSecondWorkspace` / `Seed:DemoSecondAdminEmail` | `true` / `admin@acme.local` | With demo data: second workspace "Acme Research" (admin password = `Seed:AdminPassword`) |
| `Seed:DemoData` | `false` (`true` in Development) | Demo user, surveys, responses and report |
| `Seed:DemoUserEmail` / `Seed:DemoUserPassword` | `user@smartsurvey.local` / `User123!` | Demo respondent |
| `Identity:RequireConfirmedAccount` | `false` | Require e-mail confirmation before login |
| `Support:BuyMeACoffeeUsername` | `jkhy9gtjs` | Buy Me a Coffee account (`https://buymeacoffee.com/{name}`) behind the support page |
| `Support:GitHubUrl` | `https://github.com/supunsarachitha/SmartSurvay` | GitHub links in the navigation bar, admin sidebar, home page, footer and support page (empty hides them) |
| `Support:ContactEmail` | *(empty)* | "Contact" link in the footer |
| `Email:FromAddress` | `no-reply@smartsurvey.local` | Sender address of account e-mails (confirmation, password reset) |
| `Email:FromName` | *(empty = product name)* | Sender display name |
| `Email:Smtp:Host` | *(empty = e-mail disabled)* | SMTP server; without it e-mails are only logged (full text in Development) |
| `Email:Smtp:Port` / `Email:Smtp:Security` | `587` / `Auto` | `Auto` = implicit TLS on 465, otherwise STARTTLS when offered; `StartTls`, `SslOnConnect`, `None` |
| `Email:Smtp:UserName` / `Email:Smtp:Password` | *(empty)* | SMTP login (set the password via environment variable / secret store) |
| `Email:Smtp:TimeoutSeconds` | `30` | Connection and command timeout |
| `Encryption:Enabled` | `true` | Encrypt respondents' written answers, "Other" texts and browser details at rest (§7) |
| `BotProtection:Enabled` | `true` | Spam/bot checks for anonymous submissions (§11) |
| `BotProtection:Difficulty` | `50000` | Upper bound of the proof-of-work search (higher = more work per response) |
| `BotProtection:MinimumSeconds` | `3` | Faster submissions are rejected as automated |
| `BotProtection:ChallengeLifetimeHours` | `24` | How long a survey page stays valid for submitting |
| `Embedding:Enabled` | `true` | Allow `/embed/s/{slug}` survey pages in iframes on other sites (share-page snippet) |
| `Embedding:AllowedOrigins` | *(empty = any site)* | Origins allowed to embed surveys, e.g. `["https://www.example.com"]` |
| `Swagger:Enabled` | `false` (`true` in Development) | Expose `/swagger` |
| `DataProtection:KeysPath` | *(empty)* | Directory for persisted data-protection keys |
| `ReverseProxy:Enabled` | `false` | Honour `X-Forwarded-For/Proto` |
| `Https:Redirect` | `true` | Enable HTTPS redirection |
| `App:PublicBaseUrl` | *(empty = the request's address)* | Public address (`https://surveys.example.com`) used in account e-mails, survey share links, QR codes, embed snippets and join links; an invalid value stops the app at start-up |
| `Workspaces:StatusCacheSeconds` | `30` | How long each instance caches a workspace's status (0–3600, `0` = no cache) |
| `RateLimits:AuthPerMinute` | `20` | Sign-in, sign-up and join requests per IP and minute (Identity API, workspace sign-up/join API and pages) |
| `AllowedHosts` | `*` | Host names the app answers to (set in production, see §16) |
| `SMARTSURVEY_MIGRATIONS_CONNECTION` (env) | local PostgreSQL | Connection used by `dotnet ef` |

## 18. Testing

```bash
dotnet test                                   # unit + integration tests
dotnet test tests/SmartSurvey.UnitTests       # fast unit tests only
bash scripts/smoke.sh --user admin / /admin /admin/surveys   # boot the app and smoke-test pages
bash scripts/smoke.sh --user superadmin /system /system/workspaces  # users: admin, user, acme, superadmin, anon
scripts/container-smoke.sh http://localhost:8080             # smoke-test a running container stack
cd scripts/browser && npm install && npm run check           # headless-browser workspace flows (see the script)
```

* **Unit tests** cover the logic evaluator, condition matcher, answer validator, slug generator, EF model
  (cascades, JSON columns, UTC), every application service (against SQLite in-memory through the real
  `AppDbContext`), the report engine, the SVG renderer, every exporter, seeding and Blazor components (bUnit).
* **Integration tests** drive the real HTTP pipeline (`WebApplicationFactory`) against an in-memory SQLite
  database: authentication, authorization, survey lifecycle, public submission, reports and exports, users,
  audit, branding, respondent pages and security headers, health and Swagger. The test host validates DI scopes and
  registrations (like Development) and captures logs, e.g. to assert that expected API errors aren't logged as errors.
* **Workspace isolation** has dedicated tests on every level: data layer (`WorkspaceIsolationTests`: filters, save
  guard, fail-closed scope), every service used against another workspace's data (`CrossWorkspaceServiceTests`),
  super admin and sign-up services, and every API group over HTTP (`WorkspaceApiTests`: isolation, disable/enable,
  approval, role boundaries) plus the pages (`WorkspacePageTests`, incl. a sign-up through the real form).
* **Smoke test** (`scripts/smoke.sh`) starts the compiled app with demo data, logs in through the real
  Identity form and checks that pages render on the server without errors. `scripts/container-smoke.sh` accepts
  `''` as admin / super admin password to skip those checks on a stack whose passwords were changed.
* **Browser check** (`scripts/browser/workspaces.check.js`, puppeteer-core + Chrome) drives the interactive Blazor
  circuits through sign-up, settings, disabling/enabling, the System console and the survey runner against a fresh
  instance, and fails on console errors, failed requests or 5xx responses.

## 19. Deployment

**Docker:** `docker build -t smartsurvey .` produces a Linux image (non-root user, port 8080, fontconfig and
DejaVu fonts for PDF rendering). `docker-compose.yml` runs the app with PostgreSQL, persists the database and the
data-protection keys in volumes, and configures the first workspace admin via `ADMIN_EMAIL` / `ADMIN_PASSWORD` and the
first super admin via `SUPERADMIN_EMAIL` / `SUPERADMIN_PASSWORD` (both default to `ChangeMe123!` — change them). The keys are
stored unencrypted on their volume (the container logs a warning about it): keep the volume private, or configure
key encryption (`ProtectKeysWithCertificate`) for stricter environments.

**Production checklist**

- [ ] Strong `Seed:SuperAdminPassword` and `Seed:AdminPassword` (or create the accounts once and set `Seed:CreateSuperAdmin` / `Seed:CreateAdmin` to `false`); change them after the first sign-in
- [ ] Decide on self-service sign-up (System → Settings: on, off, or with approval) and set a support e-mail
- [ ] `App:PublicBaseUrl` (Docker: `PUBLIC_BASE_URL`) and `AllowedHosts` (Docker: `ALLOWED_HOSTS`) set to the site's address
- [ ] Several app instances: consider a lower `Workspaces:StatusCacheSeconds` so disabling a workspace takes effect everywhere quickly
- [ ] `Seed:DemoData=false`
- [ ] Connection string from a secret store
- [ ] `DataProtection:KeysPath` on persistent storage (or a shared key ring when scaling out) — it holds the keys that
      decrypt stored answers: **back it up together with every database backup** and restore both together
- [ ] TLS at the proxy + `ReverseProxy:Enabled=true`, or Kestrel HTTPS
- [ ] Decide on `Swagger:Enabled`
- [ ] Database backups; apply migrations as part of the release (`dotnet ef migrations script --idempotent`)
- [ ] `Email:Smtp:*` for account e-mails (confirmation, password reset) — or reset passwords from the Users page
- [ ] `Embedding:AllowedOrigins` (or `Embedding:Enabled=false`) if surveys should only be embedded by your own sites

**CI:** `.github/workflows/ci.yml` builds and tests on every push/PR and fails on known vulnerable packages;
checks that the model has no changes without a migration and applies the migrations to a PostgreSQL service
container; builds the Docker image, starts it with docker compose and runs `scripts/container-smoke.sh`.

## 20. Extending SmartSurvey

* **New export format** — implement `IReportExporter` (Infrastructure/Exports), register it in
  `Infrastructure/DependencyInjection.cs`, add the enum value to `ExportFormat` (+ content type/extension).
  The report service, API and UI pick it up.
* **New widget type** — add a `WidgetType` value (+ display helpers in `QuestionTypeExtensions`), compute
  it in `ReportEngine`, say which questions it supports in `ReportDesign.Supports` (builder) and render it in
  `WidgetView` (UI) — exporters work automatically because they consume the generic `WidgetResult` (stats, chart,
  tables).
* **New question type** — add a `QuestionType` value, classify it in `QuestionTypeExtensions`
  (`IsText/IsNumeric/IsChoice/IsDate`), extend `ResponseValidator`, `QuestionField` (runner input), the builder
  settings panel and, if needed, report aggregation.
* **New condition operator** — extend `ConditionOperator`, `ConditionMatcher` and `SupportedOperators`.

## 21. Troubleshooting

| Symptom | Fix |
|---|---|
| `Connection refused` on start-up | PostgreSQL not running / wrong connection string; or use SQLite mode |
| `28P01 password authentication failed` | Create the `smartsurvey` role (§4.2) or fix the password |
| Admin login fails on a fresh database | `Seed:AdminPassword` was empty — set it and restart |
| No super admin after upgrading | `Seed:SuperAdminPassword` (Docker: `SUPERADMIN_PASSWORD`) was empty, or the address already belongs to an account (it is never promoted) — the start-up log says which |
| "Your workspace is unavailable" after signing in | A super admin disabled the workspace (or it waits for approval) — enable/approve it in System → Workspaces |
| `403` "Registration happens per workspace" from `/api/auth/register` | Use `POST /api/v1/public/workspaces` (new workspace) or `/api/v1/public/workspaces/{slug}/register` (join) |
| Logged out after every restart (Docker) | Configure `DataProtection:KeysPath` on a volume (compose does this) |
| Answers show "[encrypted answer — the key to read it is not available]" | The data-protection key ring is missing or from another installation (e.g. a database restored without its `keys` volume). Restore the matching key backup into `DataProtection:KeysPath` and restart |
| PDF export fails in a custom Linux image | Install `libfontconfig1` and a font package (see Dockerfile) |
| Docker Desktop "Linux engine" errors on Windows | Enable WSL 2 (`wsl --install`, reboot) or use native PostgreSQL |
| `409 Conflict` when saving a survey | Someone else saved it — reload the builder and re-apply your changes |
| Password-reset / confirmation e-mails don't arrive | `Email:Smtp:Host` is empty (the log says so) or the server rejects the sender — check `Email:FromAddress`, credentials and `Email:Smtp:Security`; meanwhile set a password from the Users page |
| An embedded survey shows "refused to connect" | Embedding is disabled or the site isn't in `Embedding:AllowedOrigins`; the snippet must use `/embed/s/{slug}` |

## 22. Licensing notes

* **SmartSurvey itself** is source-available under the **PolyForm Noncommercial License 1.0.0** ([`LICENSE.md`](../LICENSE.md)):
  free for non-commercial purposes; commercial use needs a separate license from the copyright holder — the repository
  owner, Supun ([github.com/supunsarachitha](https://github.com/supunsarachitha)) — who keeps all rights, including
  commercial use.
* **QuestPDF** is used under the *Community* license (free for individuals, open-source projects and
  companies with < USD 1M annual gross revenue). Larger organisations need a commercial QuestPDF license.
* Bootstrap, Bootstrap Icons (MIT), Inter (SIL OFL 1.1), ClosedXML (MIT), QRCoder (MIT), MailKit/MimeKit (MIT),
  FluentValidation (Apache 2.0), Npgsql (PostgreSQL License), Swashbuckle (MIT), bUnit (MIT), xUnit (Apache 2.0).
