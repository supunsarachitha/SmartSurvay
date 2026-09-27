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
