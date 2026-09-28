# SmartSurvey — notes for Claude Code

- **Progress lives in `DEVELOPMENT_PLAN.md`.** Before doing anything else in a new session, read its banner at the top.
  For multi-phase work follow the resume protocol pattern of § 10.0 (mark tasks `[~]`, commit per task, log "Next: …").
- Latest work: v2 follow-ups → 2.1.0 (.NET 10) on `feature/v2-follow-ups` incl. account settings redesign (plan § 11);
  v2.0.0 is in `main`.
  Never push unless asked. Build needs the .NET 10 SDK (`global.json`).
- Phase gate: build with 0 warnings → all tests green → `docker compose up -d --build` (leave it running) →
  `scripts/container-smoke.sh` → commit. Never run `docker compose down -v` (the volume holds real data).
- UI changes: also run `scripts/browser/buttons.check.js` against a throwaway container of the image with its own
  PostgreSQL (commands in the script's header). Development builds serve Blazor's scripts differently and SQLite
  completes queries synchronously, so both hide failures that only the published app on PostgreSQL shows.
- Conventions, quality gates and UI standards: `DEVELOPMENT_PLAN.md` § 8, 8b, 8c.
