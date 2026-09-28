# SmartSurvey — notes for Claude Code

- **Progress lives in `DEVELOPMENT_PLAN.md`.** Before doing anything else in a new session, read its "CURRENT WORK"
  banner and follow the resume protocol it links to (§ 10.0 while the multi-workspace work is open).
- Current work branch: `feature/multi-workspace`. Commit after every task; never push unless asked.
- Phase gate: build with 0 warnings → all tests green → `docker compose up -d --build` (leave it running) →
  `scripts/container-smoke.sh` → commit. Never run `docker compose down -v` (the volume holds real data).
- Conventions, quality gates and UI standards: `DEVELOPMENT_PLAN.md` § 8, 8b, 8c.
