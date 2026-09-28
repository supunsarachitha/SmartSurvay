# SmartSurvey — notes for Claude Code

- **Progress lives in `DEVELOPMENT_PLAN.md`.** Before doing anything else in a new session, read its banner at the top.
  For multi-phase work follow the resume protocol pattern of § 10.0 (mark tasks `[~]`, commit per task, log "Next: …").
- Multi-workspace v2.0.0 is complete on `feature/multi-workspace` (not merged yet). Never push unless asked.
- Phase gate: build with 0 warnings → all tests green → `docker compose up -d --build` (leave it running) →
  `scripts/container-smoke.sh` → commit. Never run `docker compose down -v` (the volume holds real data).
- Conventions, quality gates and UI standards: `DEVELOPMENT_PLAN.md` § 8, 8b, 8c.
