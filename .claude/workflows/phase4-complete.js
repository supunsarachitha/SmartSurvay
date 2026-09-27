export const meta = {
  name: 'phase4-complete',
  description: 'Complete SmartSurvey Phase 4: finish interrupted 4C/4D implementations, then independent adversarial review+fix of all four backend tracks',
  phases: [
    { title: 'Implement', detail: 'finish 4C (reporting/exports) and 4D (audit/dashboard/users/seeding)' },
    { title: 'Review', detail: 'fresh agent per track: adversarial review (incl. PostgreSQL checks) then fix' },
  ],
}

const BASE = args.baseSha
const REPO = args.repo
const WT_ROOT = args.worktreeRoot
const wt = (t) => `${WT_ROOT}/${t.key}`
const common = (t) => COMMON.replaceAll("__WT__", wt(t)).replaceAll("__BRANCH__", `phase4/${t.key}`)

const IMPL_SCHEMA = {
  type: 'object',
  properties: {
    commitSha: { type: 'string' },
    branch: { type: 'string' },
    worktreePath: { type: 'string' },
    summary: { type: 'string' },
    filesChanged: { type: 'array', items: { type: 'string' } },
    contractChanges: { type: 'array', items: { type: 'string' } },
    testsAdded: { type: 'integer' },
    totalTestsPassing: { type: 'integer' },
    buildWarnings: { type: 'integer' },
    openIssues: { type: 'array', items: { type: 'string' } },
  },
  required: ['commitSha', 'summary', 'filesChanged', 'contractChanges', 'testsAdded', 'totalTestsPassing', 'openIssues'],
}

const REVIEW_SCHEMA = {
  type: 'object',
  properties: {
    verdict: { type: 'string', enum: ['pass', 'needs-fixes'] },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          severity: { type: 'string', enum: ['critical', 'major', 'minor'] },
          file: { type: 'string' },
          title: { type: 'string' },
          detail: { type: 'string' },
          suggestedFix: { type: 'string' },
        },
        required: ['severity', 'file', 'title', 'detail', 'suggestedFix'],
      },
    },
  },
  required: ['verdict', 'findings'],
}

const COMMON = `
You are implementing ONE track of Phase 4 of the SmartSurvey project (ASP.NET Core 8, EF Core 8.0.31, Blazor Server,
PostgreSQL primary / SQLite for tests, FluentValidation 12, QuestPDF, ClosedXML).

WORKSPACE (critical): your dedicated git worktree is __WT__ (branch __BRANCH__). Your shell and tools START in the MAIN
repository (${REPO}) — you must NOT work there. Run EVERY shell command as \`cd "__WT__" && <command>\` and read / edit /
create files ONLY through absolute paths under __WT__/ (e.g. __WT__/src/SmartSurvey.Application/...). Never modify, build
in, or run git commands that change anything under the main repository ${REPO}; the orchestrator and other agents depend
on it. Other tracks are implemented concurrently in sibling worktrees, so also stay strictly inside your file ownership.
All relative paths mentioned below are relative to __WT__.

READ FIRST (do not skip):
- DEVELOPMENT_PLAN.md (§3 Architecture, §4 Data model, §8 Conventions, §8c Quality gates)
- src/SmartSurvey.Domain/** (entities, enums incl. QuestionTypeExtensions, value objects)
- src/SmartSurvey.Application/Common/*, Logic/*, Responses/ResponseDtos.cs, Responses/ResponseValidator.cs,
  Surveys/SurveyDtos.cs, Surveys/SurveyMapper.cs, and the contract files of your area
- src/SmartSurvey.Infrastructure/Persistence/** (AppDbContext, configurations, interceptor)
- tests/SmartSurvey.UnitTests/TestSupport/* and tests/SmartSurvey.UnitTests/Core/* (patterns to follow)

RULES
1. Replace the STUB implementation(s) you own; keep public class names, namespaces and interface signatures unchanged.
   Constructor parameters are yours to choose (DI resolves them). Only depend on registered services:
   IAppDbContextFactory, ICurrentUser, TimeProvider, IAuditService, IValidator<T> (FluentValidation validators are
   auto-registered from the Application assembly via AddValidatorsFromAssembly), ILogger<T>, IEnumerable<IReportExporter>,
   IReportEngine, ISvgChartRenderer, IOptions<T>; in Infrastructure additionally IDbContextFactory<AppDbContext>,
   UserManager<ApplicationUser>, RoleManager<IdentityRole<Guid>>, IServiceScopeFactory.
   Do NOT depend on another track's service implementation (it is a stub in your worktree).
2. Do NOT modify: Domain entities, AppDbContext / entity configurations / migrations, existing DTO + interface contract
   files, Program.cs, any DependencyInjection.cs, Web UI files, tests/.../TestSupport, tests/.../Core. If a change is truly
   unavoidable make the smallest ADDITIVE change and list it in contractChanges. You MAY add new files in your area.
3. Data access: one context per operation: \`await using var db = await dbFactory.CreateAsync(ct);\`. AsNoTracking for reads,
   AsSplitQuery for multi-collection Includes. Provider-portable LINQ ONLY — tests run on SQLite but production runs on
   PostgreSQL (Npgsql): no EF.Functions.ILike (use x.Prop.ToLower().Contains(term.ToLower())), no raw SQL, no
   DateTimeOffset, no client-only methods inside IQueryable expressions. Prefer simple Where/Select/GroupBy(key)+Count/
   Sum/Min/Max projections, then shape in memory. Remember SQLite is lenient where PostgreSQL is not (e.g. GroupBy over
   complex expressions, string comparison casing): write queries that are valid for both.
4. Time: TimeProvider only (\`time.GetUtcNow().UtcDateTime\`); all timestamps UTC DateTime.
5. Errors: throw NotFoundException / AppValidationException / ForbiddenException / ConflictException /
   BusinessRuleException (SmartSurvey.Application.Common) with clear user-facing messages. Admin-only operations must
   check currentUser.IsAdmin and throw ForbiddenException otherwise (defense in depth).
6. Audit: after successful mutations call IAuditService.LogAsync(AuditActions.X, "<EntityType>", id.ToString(), details).
7. Quality: file-scoped namespaces, XML doc comments on all public members, comments explaining non-obvious logic,
   small focused methods, nullable-clean, no dead code, 0 build warnings. Validators are FluentValidation
   AbstractValidator<T> classes (public or internal) in your area; call them from services and convert failures to
   AppValidationException (group errors by PropertyName).
8. Tests (xUnit + plain Assert; no FluentAssertions/Moq/NSubstitute): put them under tests/SmartSurvey.UnitTests/<Folder>/
   with namespace SmartSurvey.UnitTests.<Folder>. Use SqliteTestDatabase (implements IAppDbContextFactory, exposes
   Time (FakeTimeProvider) and CurrentUser (TestCurrentUser, admin by default; use ActAsRespondent/ActAsAnonymous)),
   RecordingAuditService, SampleSurveys. Any helper classes must live in your folder/namespace with track-specific names
   (tracks are merged later — avoid name clashes). Cover happy paths, validation failures, authorization, edge cases.
9. Verify: \`dotnet build SmartSurvey.sln -nologo\` → 0 errors AND 0 warnings; \`dotnet test SmartSurvey.sln -nologo\` →
   ALL tests green (including pre-existing ones). Iterate until both pass. Do not skip/disable tests.
10. Commit on your current worktree branch (never switch branches, never push, never touch main):
    git add -A && git commit -m "Phase 4<X>: <summary>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
    Then report commitSha (git rev-parse HEAD), branch (git rev-parse --abbrev-ref HEAD), worktreePath
    (git rev-parse --show-toplevel), files changed, any contract changes, tests added, total passing tests, open issues.
`

const TRACKS = [
  {
    key: '4A-surveys',
    folder: 'Surveys',
    spec: `
TRACK 4A — SurveyService (survey design use cases). Folder for tests: tests/SmartSurvey.UnitTests/Surveys.
OWN: src/SmartSurvey.Application/Surveys/SurveyService.cs (replace stub), new files under
src/SmartSurvey.Application/Surveys/ (e.g. Validation/SurveyDefinitionValidator.cs, SurveyDefinitionCloner.cs,
SurveyNormalizer.cs), tests under tests/SmartSurvey.UnitTests/Surveys/.

Implement every ISurveyService member (see ISurveyService.cs XML docs — they are the contract). All methods are admin-only.
- ListAsync(SurveyQuery): filters Status (null ⇒ all except Archived unless IncludeArchived), IsTemplate, Search
  (title/description/slug, case-insensitive portable). Project SurveySummaryDto with QuestionCount, CompletedResponses,
  InProgressResponses, LastResponseAt (max SubmittedAt of completed). Order by (UpdatedAt ?? CreatedAt) desc. Paging via
  PageRequest.Skip/PageSize → PagedResult.
- ListTemplatesAsync: IsTemplate surveys (not archived), newest first.
- GetAsync: load Sections, Questions(+Options), LogicRules(+Conditions) (AsSplitQuery, AsNoTracking) → SurveyMapper.ToDefinitionDto.
  NotFoundException when missing. FindBySlugAsync: same by slug (case-insensitive), null when missing.
- CreateAsync: validate (see validator below), NORMALISE then persist as Draft:
  * assign new Guids to any item with Guid.Empty (sections, questions, options, rules, conditions) BEFORE validation of references;
  * if no sections, create one "Page 1" section;
  * re-number Order 0..n-1 for sections (by current Order), questions per section, options per question;
  * generate question Codes for empty ones as Q1..Qn in display order, skipping codes already used; codes must end unique
    per survey (case-insensitive) — duplicates supplied by the client ⇒ validation error;
  * remove options from non-choice questions; trim strings;
  * slug: when empty generate from title via SlugGenerator and make unique by appending -2, -3 …; when supplied it must be
    SlugGenerator.IsValid and unused, otherwise ConflictException (taken) / AppValidationException (format);
  * map DTO → entities (use the DTO ids), Version = 1, then return GetAsync(id). Audit SurveyCreated.
- UpdateAsync(id, dto): load TRACKED graph; if dto.Version != entity.Version ⇒ ConflictException("This survey was modified by someone else…
  reload"). Validate + normalise like create. Reconcile by id: update scalar fields (NOT Status/PublishedAt/ClosedAt/
  CreatedAt/IsTemplate? — IsTemplate IS editable), add new sections/questions/options/rules/conditions, update existing
  (including moving a question to another section), delete missing ones (EF cascade removes dependents; DB cascades answers).
  Increment Version. Slug change must remain unique (exclude self). Catch DbUpdateConcurrencyException ⇒ ConflictException.
  Audit SurveyUpdated. Return fresh GetAsync.
- DeleteAsync: NotFound when missing; delete (cascade responses/reports). Audit SurveyDeleted.
- ChangeStatusAsync: allowed transitions: Draft→Published, Published→Closed, Closed→Published (reopen), any→Archived,
  Archived→Draft; same status ⇒ no-op return. Publishing requires ≥1 question and !IsTemplate (BusinessRuleException
  otherwise); set PublishedAt=now on publish, ClosedAt=now on close (clear ClosedAt on reopen). Invalid transition ⇒
  BusinessRuleException. Audit SurveyStatusChanged with "From → To".
- DuplicateAsync(id, request): deep copy of the design (NO responses) with brand-new ids for every item and all internal
  references remapped (logic targets, condition sources, condition option ids). Title = request.Title ?? "Copy of {title}"
  (truncate to 200). IsTemplate = request.AsTemplate. New unique slug. Status Draft. Audit SurveyDuplicated.
- ExportDefinitionAsync: SurveyExportDocument { SchemaVersion=1, Generator="SmartSurvey", ExportedAt=now, Survey=GetAsync }.
  Audit SurveyExported.
- ImportDefinitionAsync: validate document (non-null survey, SchemaVersion==1 else AppValidationException), remap every id
  to new Guids consistently (same cloner as Duplicate), reset Status to Draft, Version, clear slug if already taken (generate
  new), then create. Audit SurveyImported.
- IsSlugAvailableAsync(slug, exclude): case-insensitive check. CountAnswersAsync(questionId): answers count.

SurveyDefinitionValidator (FluentValidation AbstractValidator<SurveyDefinitionDto>): Title required ≤200; Description ≤4000;
Welcome/ThankYou ≤4000; OpensAt < ClosesAt when both; MaxResponses > 0 when set; section Title required ≤200, Description ≤2000;
question Text required ≤1000, Description ≤2000, Code ≤50 matching ^[A-Za-z][A-Za-z0-9_\\-]*$ when supplied; choice questions
(Radio/Checkbox/Dropdown) need ≥2 options (≥1 is too weak for radio) with Text required ≤500, Value ≤100, placeholder ≤200;
settings sanity: MinLength ≤ MaxLength, MaxLength ≤ 10000, MinValue ≤ MaxValue, MinSelections ≤ MaxSelections ≤ option count,
RatingMax 2..10, ScaleMin < ScaleMax with range within 0..100 and ≤ 11 steps? (allow ScaleMax-ScaleMin ≤ 20), MinDate ≤ MaxDate;
all ids unique across the survey (sections, questions, options, rules, conditions);
logic rules: exactly one of TargetQuestionId/TargetSectionId and it must exist; ≥1 condition; each condition's
SourceQuestionId exists and PRECEDES the target (question target: strictly earlier in display order; section target:
source lies in an earlier section); a rule may not target its own source; operator must be in
SourceQuestion.Type.SupportedOperators(); choice sources require OptionId belonging to that question; non-choice,
non-unary operators require a Value parseable for the type (number via ConditionMatcher.TryParseNumber, date via TryParseDate).
Use readable error keys like "Sections[0].Questions[1].Options" and messages a survey designer understands.

Tests (thorough): create normalisation (ids, orders, codes, default section, slug generation + uniqueness suffix, slug conflict),
validator cases (each rule above incl. logic ordering violations), update reconciliation (add/update/remove/move question,
options, rules; answers of removed question deleted), version conflict, status transitions (valid/invalid/publish rules/
timestamps), duplicate (all ids new, references remapped, no responses copied), export/import round-trip (structure equal,
ids differ, slug handling), list filters/search/paging/counts, authorization (non-admin ⇒ Forbidden), audit entries.`,
  },
  {
    key: '4B-responses',
    folder: 'Responses',
    spec: `
TRACK 4B — ResponseService (collecting answers). Folder for tests: tests/SmartSurvey.UnitTests/Responses.
OWN: src/SmartSurvey.Application/Responses/ResponseService.cs (replace stub), new files under
src/SmartSurvey.Application/Responses/ (e.g. AnswerMapper.cs, AnswerFormatter.cs, SurveyEligibilityChecker.cs), tests.
Do NOT use ISurveyService (stub in your worktree): load the survey graph yourself (Sections, Questions+Options,
LogicRules+Conditions, AsSplitQuery/AsNoTracking) and map with SurveyMapper.ToDefinitionDto.

Implement every IResponseService member (the XML docs in IResponseService.cs are the contract):
- Eligibility (shared helper): survey not found ⇒ NotFound; Survey.GetAvailability(now) ⇒ NotPublished/NotOpenYet/Closed;
  MaxResponses reached (completed count ≥ Max) ⇒ QuotaReached; !AllowAnonymous && !authenticated ⇒ LoginRequired;
  authenticated && !AllowMultipleResponses && user has a Completed response ⇒ AlreadyResponded; else Eligible.
  Friendly messages for each (e.g. "This survey opens on 1 Mar 2026 10:00 UTC.").
- ListAvailableAsync: published, non-template, open now, quota not reached; guests see only AllowAnonymous surveys;
  per-user HasCompleted/HasDraft/CanRespond; QuestionCount; EstimatedMinutes = max(1, ceil(questions*20s/60)). Order by
  PublishedAt desc. Efficient (no N+1: aggregate counts in one or two queries).
- StartOrResumeAsync(slug): slug case-insensitive. Returns SurveySessionDto with Eligibility+Message; Survey only when
  Eligible (and also when AlreadyResponded? — no: only Eligible gets the definition). For authenticated users return the
  latest InProgress draft (DraftResponseId, CurrentSectionIndex, Answers mapped to AnswerInputDto incl. selections + free text).
- SaveDraftAsync: requires authentication (ForbiddenException "Please log in to save your progress."); survey must be
  Eligible (BusinessRuleException with the eligibility message otherwise). Find draft: request.ResponseId (must exist, belong to
  the user, be InProgress and same survey — otherwise NotFound/Forbidden) or the user's latest draft for the survey, or create one
  (StartedAt=now). Replace its answers with sanitised answers: ignore unknown question ids, use ResponseValidator.Sanitize per
  question, skip null results; no required validation for drafts. CurrentSectionIndex clamped to [0, sections-1]. UpdatedAt=now,
  UserAgent truncated to 512. Return id.
- SubmitAsync: eligibility (non-Eligible ⇒ BusinessRuleException with message; LoginRequired ⇒ ForbiddenException).
  Sanitise answers (ignore unknown questions; duplicates: last wins), evaluate LogicEvaluator, DISCARD answers of hidden
  questions, validate with ResponseValidator.Validate ⇒ AppValidationException whose Errors keys are questionId.ToString() and
  values the messages. Persist atomically (use a transaction via db.Database? — IAppDbContext does not expose Database; instead
  re-check quota inside the same context immediately before SaveChanges and accept the tiny race; document it):
  authenticated user with a draft (request.ResponseId or latest draft) ⇒ convert that draft to Completed (replace answers, keep
  StartedAt); otherwise a new response (RespondentId = user id or null for anonymous, StartedAt = now). SubmittedAt=now,
  Status=Completed. Map answers: text types → TextValue, numeric → NumberValue, Date → DateValue, choice → AnswerSelections
  (+FreeText). Audit ResponseSubmitted (entity "Response"). Return SubmitResponseResult(id, survey.ThankYouMessage).
- ListMineAsync: authenticated only (else empty list); newest first; CanContinue = InProgress && survey open.
- ListForSurveyAsync (admin): NotFound if survey missing; filters Status, From/To (inclusive UTC dates on SubmittedAt ?? StartedAt),
  Search (respondent email or display name, case-insensitive); order by (SubmittedAt ?? StartedAt) desc; paging; RespondentName =
  DisplayName ?? Email ?? "Anonymous"; AnswerCount; DurationSeconds for completed.
- GetAsync: admin OR the owning respondent (else ForbiddenException); NotFound if missing. Answers in survey display order
  including unanswered questions (IsAnswered=false, DisplayValue=""). DisplayValue formatting (put in an AnswerFormatter
  helper that the report/export code could mirror): choice ⇒ option texts joined with "; " and free text as "Other: teal"
  → "Label: freetext"; Rating ⇒ "4 / 5"; Scale/Number ⇒ invariant number; Date ⇒ yyyy-MM-dd; text as is.
- DeleteAsync (admin): NotFound; delete; audit ResponseDeleted.

Tests (thorough, use SampleSurveys.CustomerFeedback + seeding through entities): eligibility matrix (draft/closed/not open yet/
quota/login required/already responded/multiple allowed), available list for guest vs user with flags, start/resume returns
draft answers, draft save create+update+clamping+ownership checks, submit happy path persisted correctly (text/number/date/
choice+free text), submit hidden-question answers discarded, submit validation errors keyed by question id, submit converts draft,
anonymous submit, quota reached after submit, list/get/delete for admin incl. filters/search/paging and ownership rules,
DisplayValue formatting, audit entries.`,
  },
  {
    key: '4C-reporting',
    folder: 'Reports',
    spec: `
TRACK 4C — Reporting engine, SVG charts, exporters and raw response export. Test folder: tests/SmartSurvey.UnitTests/Reports
(sub-namespaces allowed, e.g. SmartSurvey.UnitTests.Reports.Exports).
OWN: src/SmartSurvey.Application/Reports/ReportService.cs, ReportEngine.cs, Charts/SvgChartRenderer.cs (replace stubs) + new
files under src/SmartSurvey.Application/Reports/ (validator, engine helpers); src/SmartSurvey.Infrastructure/Exports/*
(replace the 5 exporter stubs + ResponseExportService; add shared helpers such as CsvWriter.cs, TextTable.cs); tests.

1) SvgChartRenderer (pure, deterministic, XSS-safe): kinds Bar (grouped multi-series), HorizontalBar, StackedBar, Pie, Doughnut
   (first series), Line (multi-series). viewBox="0 0 W H" + width/height attributes; nice axis ticks (1/2/5×10^n), gridlines,
   value labels (optional), legend (series names; category names for pie/doughnut, with percentages when ShowPercentages),
   long labels truncated with an ellipsis + <title> tooltip elements; XML-escape every piece of text; "No data" placeholder
   SVG for empty data or all-zero pie. Use ONLY presentation attributes (fill, stroke, font-family, font-size, text-anchor) —
   no CSS <style>, no classes, no foreignObject/filters — so QuestPDF's SVG renderer (Skia) can embed it. Honour
   SvgChartOptions (TextColor may be "currentColor"). Output must parse with XDocument.
2) ReportEngine.ExecuteAsync(definition): load survey (sections/questions/options) → SurveyDefinitionDto; build filtered response
   set: SurveyId, Status Completed (plus InProgress when Filters.IncludeInProgress), date range From/To inclusive on
   (SubmittedAt ?? StartedAt) (To ⇒ < To+1 day), answer filters evaluated with ConditionMatcher (load answers of the filter
   questions for candidate responses, map to AnswerInputDto, combine with Filters.MatchType; unknown filter question ⇒ ignored
   and mentioned in FilterSummary). FilterSummary: human-readable lines (e.g. "Completed responses only", "Submitted between
   2026-01-01 and 2026-01-31", "Q1 “Did you enjoy…” is “No”"). Then per widget (ordered) produce WidgetResult; a widget whose
   question is missing/deleted gets Error="The question used by this widget no longer exists." (never throw for one widget):
   - SummaryStats: KPIs as StatItems: Responses (filtered), Completed, In progress (when included), Completion rate
     (completed/(completed+in progress) over ALL responses of the survey), Average completion time (mm:ss), First response,
     Last response.
   - QuestionTable / BarChart / HorizontalBarChart / PieChart / DoughnutChart on a question:
     choice ⇒ distribution over options (Count, Percent of respondents who answered; checkbox percentages may exceed 100% ⇒ Note),
     honour Settings.SortOrder and TopN (note dropped categories), footer total; IncludeFreeText ⇒ ExtraTables with the free-text
     answers of AllowsFreeText options (Option, Answer; latest first, MaxRows). Rating/Scale ⇒ every integer value in range
     (zero-filled) + Stats (Count, Mean, Median, Min, Max, Std dev; for Scale 0–10 also NPS = %promoters(9-10) − %detractors(0-6)).
     Number ⇒ Stats + histogram with ≤10 equal-width buckets. Date ⇒ distribution per month (yyyy-MM). Text ⇒ Stats (answer count)
     and a table of the latest MaxRows answers (charts on text questions ⇒ Error telling the user to use Text responses).
     Chart widgets fill Chart (Kind per widget type) and Table when Settings.ShowDataTable.
   - LineChart: responses over time grouped by Settings.TimeGrouping (Day/Week starting Monday/Month) on SubmittedAt ?? StartedAt,
     zero-filled between first and last bucket (cap 400 buckets), Chart Kind Line + Table.
   - CrossTab: both questions must be choice questions (else Error); rows = primary options, columns = secondary options,
     cell = number of responses that selected both; row/column totals; Chart StackedBar (series per secondary option) + Table.
   - TextResponses: latest MaxRows text answers (for choice questions: the free texts), columns [Submitted (yyyy-MM-dd HH:mm), Answer].
   - RawResponses: columns Submitted, Respondent (email or "Anonymous"), then one column per question (Settings.ColumnQuestionIds
     or all questions in display order; header "Q1 · text…"), formatted values (choice labels "; " + "Label: freetext",
     numbers invariant, dates yyyy-MM-dd); latest MaxRows responses; Note when truncated.
   Aggregate choice counts DB-side (GroupBy OptionId + Count over AnswerSelections joined to the filtered response ids);
   numeric values: select only the NumberValue column. Keep queries PostgreSQL-compatible. Title fallback = question text or
   widget type display name. Numbers formatted with InvariantCulture ("0.#" / percentages "0.0%").
3) ReportService: admin-only CRUD (List with SurveyTitle/WidgetCount, search, SurveyId filter, paging; Get; Create; Update with
   widget reconciliation by id; Delete; Duplicate "Copy of …"), validation via a FluentValidation ReportDefinitionValidator
   (Name required ≤200, Description ≤2000, SurveyId exists (checked in service), widgets: Title ≤200, question ids must belong
   to the survey, RequiresQuestion/RequiresSecondaryQuestion honoured, MaxRows 1..1000, TopN ≥1 when set; filters: From ≤ To,
   filter questions belong to survey). BuildDefaultAsync(surveyId): SummaryStats + LineChart + one widget per question
   (Radio/Dropdown with ≤6 options ⇒ PieChart, other choice ⇒ HorizontalBarChart, Rating/Scale ⇒ BarChart, Number/Date ⇒
   QuestionTable, text ⇒ TextResponses) with sensible titles — NOT saved. RunAsync, PreviewAsync (validate first), ExportAsync:
   run, pick exporter by Format from IEnumerable<IReportExporter> (BusinessRuleException if none), ExportFile(content,
   format.ContentType(), ExportFormatExtensions.BuildFileName(reportName, format, now)), audit ReportExported. Audit create/
   update/delete.
4) Exporters (Infrastructure/Exports), all consuming ReportResult:
   - CsvReportExporter: UTF-8 with BOM, RFC 4180 quoting, CRLF; header block (Report, Survey, Generated (UTC), Responses,
     Filters), then per widget: title line, stats as "Label,Value" rows, table header/rows/footer, extra tables, blank line.
     CSV-injection protection: prefix a single quote to text cells starting with = + - @ TAB CR (but NOT to cells that parse as
     numbers, e.g. "-5" or "-2.5"). Put the writer in a reusable CsvWriter helper.
   - TxtReportExporter: readable plain text: title banner, metadata, filters, each widget with underlined heading, stats as
     aligned "Label: value", tables as ASCII grids (+---+ borders, right-aligned numeric columns), chart widgets additionally as
     horizontal text bars (█) scaled to 40 chars. Width-safe with long cells (truncate at 60 chars with …).
   - PdfReportExporter (QuestPDF, License already set to Community in DI; also set it in a static constructor for tests):
     A4 portrait, 1.5cm margins, Lato default font; header with report name + survey + generated timestamp; footer "Page x of y"
     and "Generated by SmartSurvey"; summary panel (responses, filters); each widget: heading, StatItems as a KPI grid, chart
     rendered through ISvgChartRenderer with explicit colours (TextColor "#334155", GridColor "#e2e8f0") embedded via
     container.Svg(svgString), tables with styled header, zebra rows, right-aligned numeric columns, footer row, notes/errors in
     muted/red text. Must not throw for empty reports.
   - XlsxReportExporter (ClosedXML): "Summary" sheet (metadata + filters + KPI list), then one sheet per widget (unique, sanitised
     sheet names ≤31 chars: no []:*?/\\), header row bold with fill, numeric columns written as numbers, footer bold, free-text
     tables below, autofit columns (cap width), freeze header row.
   - JsonReportExporter: System.Text.Json indented, camelCase, enums as strings.
   - ResponseExportService (admin-only): raw data for a survey (NotFound if missing): columns ResponseId, Status, StartedAt,
     SubmittedAt (ISO 8601 UTC), DurationSeconds, RespondentEmail (or empty), then one column per question in display order
     (header "Q1 - question text"), formatted values like RawResponses; CSV (reuse CsvWriter with injection protection), XLSX
     (one sheet "Responses" with typed numbers/dates), JSON (array of objects keyed by question code); completed only unless
     includeInProgress; file name via BuildFileName("{survey title} responses"); audit ResponsesExported; unsupported format ⇒
     BusinessRuleException.
Tests (thorough): renderer (every kind parses as XML, escaping of "<script>" labels, empty/zero data placeholder, legend,
percentages), engine against seeded data (choice counts/percentages, checkbox >100% note, free-text extra table, sort/TopN,
rating zero-fill + stats + NPS, number histogram, date months, text table, line chart zero-fill per grouping, crosstab totals,
raw responses, filters: date range, include in-progress, answer filters All/Any, unknown filter question, missing-question widget
error), ReportService CRUD/validation/authorization/default report/export picks exporter, each exporter (CSV quoting + BOM +
injection protection + numeric not prefixed; TXT contains titles/tables; PDF bytes start with "%PDF" and contain >1 page for a
large report; XLSX re-opened with ClosedXML has expected sheets and numeric cells; JSON parses back), ResponseExportService for
all formats. Put a realistic "big report" builder in your test folder for exporter tests.`,
  },
  {
    key: '4D-infrastructure',
    folder: 'InfrastructureServices',
    spec: `
TRACK 4D — Audit, dashboard, user administration and database seeding. Test folder:
tests/SmartSurvey.UnitTests/InfrastructureServices.
OWN: src/SmartSurvey.Application/Audit/AuditService.cs, src/SmartSurvey.Application/Dashboard/DashboardService.cs,
src/SmartSurvey.Infrastructure/Identity/UserAdminService.cs, src/SmartSurvey.Infrastructure/Persistence/Seed/DbSeeder.cs (replace
stubs) + new files in those folders (e.g. Seed/DemoSurveyFactory.cs, Seed/DemoResponseGenerator.cs); tests.

1) AuditService(IAppDbContextFactory, ICurrentUser, TimeProvider, ILogger<AuditService>):
   LogAsync never throws (catch everything except OperationCanceledException when ct is cancelled, log a warning); stamps
   Timestamp=now, UserId/UserName from ICurrentUser; truncates Action/EntityType/EntityId to 100, UserName 256, Details 4000.
   ListAsync (admin-only): filters Search (action/entity type/user name/details, case-insensitive portable), EntityType, EntityId,
   From/To inclusive UTC dates; newest first; paging.
2) DashboardService(IAppDbContextFactory, ICurrentUser, TimeProvider) admin-only: fill every DashboardSummaryDto field: survey
   counts (non-templates; Total excludes Archived? — Total = all non-template surveys, plus Published/Draft/Closed counts),
   completed/in-progress responses, ResponsesLast7Days, CompletionRate = completed/(completed+inProgress) (0 when none),
   AverageDurationSeconds of completed (compute in memory from StartedAt/SubmittedAt of completed responses, or via
   aggregate if portable), TotalUsers, TotalReports, ResponsesPerDay for the last 30 days INCLUDING today, zero-filled, oldest first
   (load SubmittedAt values ≥ start and group in memory by UTC date), TopSurveys (5, by completed responses, as SurveySummaryDto
   with counts), RecentResponses (10 latest completed with survey title and respondent display name/email or "Anonymous").
   Keep the number of queries small.
3) UserAdminService (Infrastructure) — admin-only. IMPORTANT Blazor Server note: services are scoped per circuit, so do not
   hold a long-lived UserManager: inject IServiceScopeFactory and create a scope per operation to resolve
   UserManager<ApplicationUser>/RoleManager<IdentityRole<Guid>>; use IDbContextFactory<AppDbContext> (which exposes Users,
   UserRoles, Roles) for listing with roles in few queries. ListAsync: Search (email/display name), Role filter, order by
   CreatedAt desc, paging, IsLockedOut (LockoutEnd > now), ResponseCount (completed). GetAsync. CreateAsync: validate email
   format/required, password via UserManager (IdentityResult errors ⇒ AppValidationException keyed "Password"/"Email"), duplicate
   email ⇒ ConflictException, EmailConfirmed=true, DisplayName, roles default [User], roles must be subset of AppRoles.All (else
   AppValidationException). SetRolesAsync: roles subset of AppRoles.All; an admin cannot remove their own Admin role; the last
   remaining admin cannot lose Admin (BusinessRuleException). LockAsync: cannot lock self; SetLockoutEnabled(true),
   SetLockoutEndDate(DateTimeOffset.MaxValue), UpdateSecurityStamp (invalidates sessions). UnlockAsync: clear lockout + reset
   failed count. DeleteAsync: cannot delete self, cannot delete last admin; responses remain (FK SetNull). Audit every mutation
   (UserCreated/UserRolesChanged/UserLocked/UserUnlocked/UserDeleted, entity "User").
4) DbSeeder (Infrastructure; ctor may take UserManager<ApplicationUser>, RoleManager<IdentityRole<Guid>>, AppDbContext or
   IDbContextFactory<AppDbContext>, IOptions<SeedOptions>, TimeProvider, ILogger<DbSeeder>). SeedAsync is idempotent:
   a) ensure every AppRoles.All role exists;
   b) if SeedOptions.CreateAdmin and AdminPassword is non-empty and NO user is in the Admin role ⇒ create AdminEmail
      (EmailConfirmed, DisplayName "Administrator", roles Admin+User); log a warning when an admin is needed but no password set;
   c) if SeedOptions.DemoData: ensure demo user (DemoUserEmail/DemoUserPassword, DisplayName "Demo User", role User); and ONLY IF
      the Surveys table is empty create demo content through entities (NOT through SurveyService):
      • "Customer Satisfaction Survey" — Published, AllowAnonymous, 3 sections, uses EVERY question type (short/long text, radio,
        checkbox with "Other (please specify)" free-text option, dropdown with "Other", number, email, date, rating, NPS scale 0–10
        with labels), required flags, help texts, codes Q1.., logic: e.g. show "What could we improve?" (LongText) when overall
        satisfaction radio ∈ {Dissatisfied, Very dissatisfied} (Any rule), show section "Product usage" only when "Have you used our
        product?" = Yes, hide Q "Company size" when role = Student; welcome + thank-you messages.
      • "Employee Engagement Pulse" — Published, login required, one response per user, Likert-style radio questions, checkbox
        benefits with Other, department dropdown, rating.
      • "Event Feedback" — IsTemplate (Draft), a reusable template.
      • "Product Roadmap Input" — Draft.
      • Deterministic demo responses (new Random(42)) spread over the last 60 days relative to TimeProvider now: ~150 completed
        + ~8 in-progress for the customer survey (mostly anonymous; a few by the demo user) and ~45 for the engagement survey.
        Answers must be realistic and VALID: only answer questions that are visible for that response (use LogicEvaluator on the
        survey's SurveyDefinitionDto built via SurveyMapper), respect settings/required flags so that every completed response passes
        ResponseValidator.Validate; realistic free texts from small phrase lists; durations 1–12 minutes.
      • A sample report "Customer Satisfaction Overview" for the customer survey with widgets: SummaryStats, LineChart (Day),
        PieChart on satisfaction, HorizontalBarChart on features checkbox, BarChart on NPS scale, CrossTab (satisfaction × region),
        TextResponses on the improvement question.
      Set CreatedById to the admin id when available. Save efficiently (AddRange, one SaveChanges per aggregate batch).
   Log what was seeded.
Tests (thorough): audit never throws on failure (e.g. disposed factory/connection), truncation, list filters/paging/admin check;
dashboard numbers on seeded data incl. zero-filled 30-day series and completion rate; user admin with a real Identity stack on
SQLite in-memory (build a ServiceCollection in your test folder: AddLogging, AddDataProtection? (not needed for UserManager
without tokens — add AddDefaultTokenProviders only if required), AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>(), AppDbContext on the shared SQLite connection, IDbContextFactory<AppDbContext>,
ICurrentUser, IAuditService, TimeProvider) covering create/duplicate/validation, roles rules (self/last admin), lock/unlock,
delete, list filters; DbSeeder: seeds roles/admin/demo idempotently (run twice ⇒ no duplicates), no admin without password,
EVERY seeded completed response passes ResponseValidator against its survey (visibility-aware), logic rules reference valid
questions, report widgets reference questions of the survey.`,
  },
]

// ---- Phase 4 completion run (after a usage-limit interruption) -----------------------------------
// 4A and 4B are implemented (and merged into main); they only need the independent review+fix.
// 4C and 4D were interrupted mid-implementation: their worktrees contain partial, uncommitted work
// and have been updated with the latest main (4A, 4B, branding).
const MODE = {
  '4A-surveys': { mode: 'review', implSha: args.implShas['4A-surveys'], reviewBase: args.originalBase },
  '4B-responses': { mode: 'review', implSha: args.implShas['4B-responses'], reviewBase: args.originalBase },
  '4C-reporting': { mode: 'implement', reviewBase: args.bases['4C-reporting'] },
  '4D-infrastructure': { mode: 'implement', reviewBase: args.bases['4D-infrastructure'] },
}

const RESUME_NOTE = (t) => `
RESUMING AN INTERRUPTED ATTEMPT: a previous agent started this track and was cut off by a usage limit. Your worktree
${wt(t)} may contain its PARTIAL, UNCOMMITTED work (see \`cd "${wt(t)}" && git status\`). Review it critically: keep what is
correct and good, fix or rewrite what is not, and complete everything the spec requires. The worktree has also been updated
with the latest main, which now contains the finished SurveyService (4A), ResponseService (4B) and a new branding feature:
IBrandingService (SmartSurvey.Application.Branding; GetAsync() returns BrandingDto with ProductName/Tagline/IconName/LogoUrl),
and ReportResult.ProductName. You may use the real SurveyService/ResponseService in tests where helpful.
${t.key === '4C-reporting' ? `EXTRA FOR 4C: ReportService must set ReportResult.ProductName from IBrandingService.GetAsync() (inject IBrandingService —
it is scoped) in RunAsync/PreviewAsync/ExportAsync, and every exporter must use report.ProductName wherever the product is
named (PDF footer "Generated by {ProductName}", CSV/TXT/XLSX metadata "Generated by", JSON includes it automatically). In unit
tests construct BrandingService with BrandingCache (see tests/SmartSurvey.UnitTests/Branding/BrandingServiceTests.cs) or write a
tiny fake IBrandingService in your test folder.` : ''}
${t.key === '4D-infrastructure' ? `EXTRA FOR 4D: IAppDbContext now also exposes BrandingSettings. Do not seed branding (defaults come from configuration)
and leave branding presentation to the UI.` : ''}
`

const REVIEW_FIX = (t, implSha, reviewBase, implSummary) => `${common(t)}
${t.spec}

YOU ARE THE INDEPENDENT REVIEWER-AND-FIXER for track ${t.key}. A different engineer implemented it; you did not write it.
The track's implementation is the diff \`git diff ${reviewBase} ${implSha}\` (run in your worktree; for 4A/4B the worktree has
since been fast-forwarded to main, which includes that commit plus other merged work — review ONLY this track's files, but
make sure they integrate with the rest). Implementer summary: ${implSummary}

STEP 1 — ADVERSARIAL REVIEW. Hunt for REAL defects (not style nits): spec requirements missing or wrong; logic bugs; incorrect
edge cases; EF Core queries that work on SQLite but FAIL or behave differently on PostgreSQL/Npgsql (untranslatable expressions,
client evaluation, DateTime Kind, GroupBy over complex keys, case sensitivity, ordering by nullable columns); EF
change-tracking/reconciliation bugs (Modified instead of Added, orphaned rows, cascade surprises, duplicate tracking);
missing authorization; security (XSS in generated SVG/markup, CSV injection, info leaks, unbounded input); concurrency;
unbounded queries / N+1; exceptions leaking instead of AppException subtypes; weak or missing tests for important behaviour.
A local PostgreSQL 16 server is available (Host=localhost;Port=5432;Username=smartsurvey;Password=smartsurvey; the role has
CREATEDB): for query-heavy services, run a representative subset of the service tests against a temporary PostgreSQL database
(create a uniquely named database, run, then DROP it — never touch the "smartsurvey" database) to prove the queries translate.
STEP 2 — FIX every real critical/major issue and cheap minor ones; add regression tests. Build (0 warnings) and run ALL tests
(green). Commit "Phase 4${t.key.slice(1, 2)} review fixes: <summary>" with the Co-Authored-By trailer (no empty commit if nothing
needed fixing — then report the existing HEAD).
Report commitSha (HEAD after your work), summary listing every finding as "[severity] title — fixed | rejected(reason)",
files changed, tests added, total tests passing, open issues.`

phase('Implement')
const results = await pipeline(
  TRACKS,
  // Stage 1 — finish interrupted implementations (4C, 4D); pass-through for already implemented tracks.
  (t) => {
    const m = MODE[t.key]
    if (m.mode === 'review') {
      return { commitSha: m.implSha, summary: 'Implemented in the previous run (see commit message and code).' }
    }
    return agent(`${common(t)}\n${t.spec}\n${RESUME_NOTE(t)}\n\nWhen finished, return the structured report.`, {
      label: `impl:${t.key}`,
      phase: 'Implement',
      schema: IMPL_SCHEMA,
    })
  },
  // Stage 2 — independent adversarial review + fix by a fresh agent.
  (impl, t) => {
    if (!impl || !impl.commitSha) return { key: t.key, final: null, impl }
    const m = MODE[t.key]
    return agent(REVIEW_FIX(t, impl.commitSha, m.reviewBase, impl.summary), {
      label: `review+fix:${t.key}`,
      phase: 'Review',
      schema: IMPL_SCHEMA,
    }).then((fix) => ({ key: t.key, final: fix?.commitSha || impl.commitSha, impl, fix }))
  },
)

return results.map((r, i) => r ? r : { key: TRACKS[i].key, final: null })
