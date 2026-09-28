<div align="center">

# 📋 SmartSurvey

**Design smart surveys with conditional logic, collect responses anywhere, and turn them into beautiful, exportable reports.**

ASP.NET Core 8 · Blazor (Interactive Server) · EF Core 8 · PostgreSQL · QuestPDF

</div>

---

## ✨ Highlights

- **Survey builder** — 10 question types (text, paragraph, radio, checkbox, dropdown, number, e-mail, date, star rating,
  linear scale/NPS), answer options, combined *"Other → free text"* options, multi-page sections, per-question settings.
- **Conditional logic** — show/hide questions and whole pages based on earlier answers (10 operators, All/Any),
  evaluated live in the browser and re-validated on the server.
- **Great respondent experience** — mobile-friendly runner, progress bar, live validation, auto-saved drafts with
  resume, anonymous links, QR codes and embedding in any website.
- **Dynamic reports** — KPIs, distribution tables, bar/pie/doughnut/line charts, cross-tabs, NPS, text answers and raw
  grids with date and answer filters, live preview, and **PDF / CSV / TXT / Excel / JSON** export.
- **Administration** — dashboard, response browser, user & role management (incl. password reset), audit log and
  branding (product name, tagline, logo).
- **REST API** — every capability over `/api/v1` with bearer tokens, Swagger and ProblemDetails.
- **Production-ready** — Clean Architecture, FluentValidation, SMTP account e-mails, rate limiting, security headers,
  health checks, dark mode, Docker, CI, 750+ automated tests.
- Plus a **FAQ** page and a **Buy Me a Coffee** support page.

## 🚀 Quick start

```bash
# 1. Prerequisites: .NET 8 SDK + PostgreSQL 16 (native or `docker compose up -d db`)
#    Create the role/database once:  CREATE ROLE smartsurvey LOGIN PASSWORD 'smartsurvey' CREATEDB;
#                                    CREATE DATABASE smartsurvey OWNER smartsurvey;
# 2. Run
dotnet tool restore
dotnet run --project src/SmartSurvey.Web
```

Open <https://localhost:7047> and sign in with **admin@smartsurvey.local / Admin123!** (Development seed data includes
example surveys, ~200 responses and a sample report). API docs: `/swagger`.

No PostgreSQL? Run with SQLite:

```bash
Database__Provider=Sqlite ConnectionStrings__DefaultConnection="Data Source=smartsurvey.db" \
  dotnet run --project src/SmartSurvey.Web
```

(PowerShell: `$env:Database__Provider="Sqlite"; $env:ConnectionStrings__DefaultConnection="Data Source=smartsurvey.db"`.)

Everything in containers: `docker compose up --build` → <http://localhost:8080> (admin password `ChangeMe123!` unless
you set `ADMIN_PASSWORD`).

## 🧱 Architecture

```
src/SmartSurvey.Domain          entities, enums, value objects
src/SmartSurvey.Application     services, DTOs, validation, conditional logic, report engine, SVG charts
src/SmartSurvey.Infrastructure  EF Core (PostgreSQL/SQLite), migrations, exporters, Identity, seeding
src/SmartSurvey.Web             Blazor UI + Minimal API + Identity
tests/                          unit (xUnit, bUnit, SQLite) and integration (WebApplicationFactory) tests
```

The Blazor UI and the REST API share one Application layer, so business rules are implemented exactly once.

## 📚 Documentation

- **[docs/DOCUMENTATION.md](docs/DOCUMENTATION.md)** — complete technical & user documentation (architecture, schema,
  logic semantics, reporting, exports, API, configuration, security, testing, deployment, extending)
- **[docs/examples](docs/examples)** — API examples (`.http` file, survey/response/report JSON payloads)
- **[CHANGELOG.md](CHANGELOG.md)** — release notes
- **[DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md)** — the phased development plan and progress log

## 🧪 Tests

```bash
dotnet test                                                       # unit + integration tests
bash scripts/smoke.sh --user admin / /admin /admin/surveys /admin/reports
scripts/container-smoke.sh http://localhost:8080                  # against a running container stack
```

## ☕ Support

If SmartSurvey saves you time, consider [buying me a coffee](https://www.buymeacoffee.com/smartsurvey) — or visit the
in-app **Support us** page.

## 📄 License notes

SmartSurvey uses [QuestPDF](https://www.questpdf.com/) under its Community license (free for individuals, open source
and companies under USD 1M annual revenue). See [DOCUMENTATION.md §22](docs/DOCUMENTATION.md#22-licensing-notes) for
all third-party licenses.
