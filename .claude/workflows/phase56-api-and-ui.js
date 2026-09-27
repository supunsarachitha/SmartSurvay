export const meta = {
  name: 'phase56-api-and-ui',
  description: 'Implement SmartSurvey REST API (+integration tests) and five Blazor UI tracks in parallel git worktrees, adversarially review each, then fix',
  phases: [
    { title: 'Implement', detail: 'API + 5 UI tracks in parallel worktrees' },
    { title: 'Review', detail: 'fresh agent: adversarial review then fix (correctness, UX, security)' },
  ],
}

const BASE = args.baseSha
const REPO = args.repo
const WT_ROOT = args.worktreeRoot
const wt = (t) => `${WT_ROOT}/${t.key}`

const IMPL_SCHEMA = {
  type: 'object',
  properties: {
    commitSha: { type: 'string' },
    branch: { type: 'string' },
    summary: { type: 'string' },
    filesChanged: { type: 'array', items: { type: 'string' } },
    contractChanges: { type: 'array', items: { type: 'string' } },
    testsAdded: { type: 'integer' },
    totalTestsPassing: { type: 'integer' },
    smokeResult: { type: 'string' },
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

const common = (t) => `
You are implementing ONE track of Phases 5/6 of SmartSurvey — a full-stack survey app (ASP.NET Core 8, Blazor Web App with
Interactive Server render mode, EF Core 8, PostgreSQL/SQLite, Bootstrap 5.3.8 + Bootstrap Icons 1.13 + Inter font).
The backend (domain, EF Core, all application services, exporters, seeding) is COMPLETE and tested. You build on it.

WORKSPACE (critical): your dedicated git worktree is ${wt(t)} (branch phase56/${t.key}). Your shell and tools START in the MAIN
repository (${REPO}) — you must NOT work there. Run EVERY shell command as \`cd "${wt(t)}" && <command>\` and read / edit /
create files ONLY through absolute paths under ${wt(t)}/. Never modify, build in, or run state-changing git commands in the
main repository. Five other tracks run concurrently in sibling worktrees: stay strictly inside your file ownership.
Relative paths below are relative to ${wt(t)}.

READ FIRST (do not skip): DEVELOPMENT_PLAN.md (§3 Architecture, §5 Features, §6 Routes, §8 Conventions, §8b UI design standard,
§8c Quality gates); src/SmartSurvey.Web/Program.cs, Infrastructure/*.cs (WebSetup: policies, rate limits, SupportOptions;
CurrentUser; BrowserInterop; UiFormat; ApiExceptionHandler), Components/App.razor, Routes.razor, _Imports.razor, Layout/*,
Shared/* (reuse these components!), wwwroot/app.css (the design system — learn its classes), wwwroot/js/app.js; the
Application contracts you consume (DTOs + interfaces under src/SmartSurvey.Application/**) AND their implementations (to know
behaviour, validation messages and thrown exceptions); src/SmartSurvey.Domain/Enums/* (incl. QuestionTypeExtensions display
names / SupportedOperators / widget helpers); tests/SmartSurvey.UnitTests/TestSupport/*.

GENERAL RULES
1. Ownership: only create/modify the files listed in your track spec. NEVER edit: wwwroot/app.css, wwwroot/js/app.js, Layout/*,
   Shared/*, root Components/_Imports.razor, App.razor, Routes.razor, Program.cs, Infrastructure/*.cs, Application/Domain/
   Infrastructure projects, migrations, TestSupport. If you believe a change there is essential, do NOT make it — describe it in
   openIssues. You MAY add per-folder _Imports.razor files inside your own folders and component-scoped CSS files
   (MyComponent.razor.css) next to your own components for styling that app.css lacks.
2. Blazor: static SSR by default; add \`@rendermode InteractiveServer\` to pages that need interactivity. Inject application
   services directly (@inject ISurveyService Surveys) — never call the HTTP API from Blazor. Interactive pages prerender:
   OnInitializedAsync runs on the server during prerender and again when interactive — keep it idempotent; JS interop
   (BrowserInterop) only in event handlers / OnAfterRenderAsync. Catch AppException subtypes and show friendly messages
   (ErrorAlert inline or ToastService on admin pages); map AppValidationException.Errors to inline field messages where
   relevant; NotFoundException ⇒ EmptyState "not found". Use CancellationToken where cheap. Dispose/unsubscribe properly.
3. UX standard (the user explicitly asked for a user-friendly, attractive, PROFESSIONAL GUI): use PageHeader on every page
   (title, subtitle, breadcrumb for admin pages, actions), cards, the design-system classes, Bootstrap Icons on actions,
   LoadingSpinner/skeletons while loading, EmptyState for empty lists (with a call-to-action), ConfirmDialog before destructive
   actions, toasts after saves on admin pages, busy buttons (spinner + disabled) during async work, inline validation
   messages, helpful hints/tooltips, keyboard accessibility (labels bound to inputs, aria-labels on icon buttons, focus states),
   responsive layouts (test mentally at 375px and 1440px widths), consistent spacing (Bootstrap gap/g-* utilities), status badges
   via StatusBadge, timestamps via LocalDateTime, numbers via UiFormat. No lorem ipsum, no placeholder TODOs, no inline <style>
   blocks. Polish matters: this is what the user sees.
   BRANDING: the product name, tagline and icon/logo are customisable by admins. NEVER hard-code "SmartSurvey" in user-facing
   UI text: inject SmartSurvey.Application.Branding.IBrandingService and use (await Branding.GetAsync()).ProductName / .Tagline,
   and render the logo/icon with the shared <BrandMark /> component (params ShowName, Size "sm"|"lg"). PageHeader already
   appends the product name to browser titles.
4. Security: never render user text as MarkupString (Blazor escapes by default — keep it that way; ChartView SVG comes from the
   trusted renderer). Respect authorization: admin pages live in Components/Pages/Admin/** (the folder _Imports applies
   AdminLayout + [Authorize(Policy = "Admin")]); user pages use @attribute [Authorize].
5. Tests: add bUnit (v1.40 API: \`using var ctx = new TestContext();\` — or inherit Bunit.TestContext — ctx.Services.Add…,
   ctx.RenderComponent<T>(p => p.Add(x => x.Param, value)), cut.Find/FindAll, element.Change/Click/Input, cut.WaitForAssertion)
   tests for your key interactive components under tests/SmartSurvey.UnitTests/Web/<Folder>/ (namespace
   SmartSurvey.UnitTests.Web.<Folder>) with hand-written fakes of the service interfaces in that folder (unique class names).
   For components using BrowserInterop: \`ctx.JSInterop.Mode = JSRuntimeMode.Loose; ctx.Services.AddScoped<BrowserInterop>();\`;
   ToastService: \`ctx.Services.AddScoped<ToastService>()\`; ISvgChartRenderer: register the real SvgChartRenderer; auth:
   \`var auth = ctx.AddTestAuthorization(); auth.SetAuthorized("admin@test.local"); auth.SetRoles("Admin");\`.
   Pages that inject NavigationManager work with bUnit's FakeNavigationManager.
6. Verify, iterating until all pass:
   a) \`cd "${wt(t)}" && dotnet build SmartSurvey.sln -nologo\` → 0 errors, 0 warnings;
   b) \`cd "${wt(t)}" && dotnet test SmartSurvey.sln -nologo\` → ALL tests green;
   c) prerender smoke test of your pages with the real app + real services + demo data (SQLite):
      \`cd "${wt(t)}" && bash scripts/smoke.sh --port ${t.port} --user <admin|user|anon> <paths…>\` (read scripts/smoke.sh; use
      real ids from the demo data: e.g. fetch /admin/surveys first and grep hrefs, or query the smoke DB at .smoke/smoke-${t.port}.db
      with the sqlite3 CLI if available). Every page must print OK. Include the output summary in smokeResult.
7. Commit on your worktree branch (never switch branches / push / touch main):
   \`cd "${wt(t)}" && git add -A && git commit -m "Phase <5|6x>: <summary>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"\`
   Report commitSha (git rev-parse HEAD), branch, files changed, contract changes (should be none), tests added, total tests
   passing, smoke result, open issues.
`

const TRACKS = [
  {
    key: '5-api',
    folder: 'Api',
    port: 5201,
    spec: `
TRACK 5 — REST API endpoints, API examples and integration tests.
OWN: src/SmartSurvey.Web/Api/** (replace EndpointStubs.cs with one file per group: SurveyEndpoints.cs, ResponseEndpoints.cs,
ReportEndpoints.cs, PublicEndpoints.cs, MeEndpoints.cs, AdminEndpoints.cs (dashboard, users, audit); you may adjust
ApiEndpoints.cs group mapping but keep the prefix /api/v1 and the authorization policies), docs/examples/** (new),
tests/SmartSurvey.IntegrationTests/** (new).

Endpoints (Minimal APIs; all logic in application services; each endpoint has .WithName(), .WithSummary()/.WithDescription(),
.Produces<T>(status) / .ProducesProblem / .ProducesValidationProblem metadata so Swagger documents them; typed results where
practical; request DTOs are the Application DTOs). Already configured in Program.cs (do not change): enums are serialised as
strings (ConfigureHttpJsonOptions + JsonStringEnumConverter), and the DEFAULT authentication scheme is a policy scheme that
uses the Identity bearer token when an "Authorization: Bearer" header is present and the Identity cookie otherwise — so
HttpContext.User and ICurrentUser (filled by CurrentUserMiddleware) reflect bearer callers on every endpoint, including
anonymous-allowed ones. Errors are mapped by ApiExceptionHandler.
  /api/v1/surveys (ApiAdmin): GET / (SurveyQuery from query string: search,status,includeArchived,isTemplate,page,pageSize),
    GET /templates, GET /{id}, POST / (201 + Location), PUT /{id}, DELETE /{id} (204), POST /{id}/status (ChangeSurveyStatusRequest),
    POST /{id}/duplicate (DuplicateSurveyRequest), GET /{id}/definition (SurveyExportDocument, as attachment-friendly JSON),
    POST /import (SurveyExportDocument body), GET /slug-available?slug=&excludeId=, GET /{id}/responses (ResponseQuery),
    GET /{id}/responses/export?format=csv|xlsx|json&includeInProgress= (file result via IResponseExportService; 400 for
    unknown format).
  /api/v1/responses (ApiUser): GET /{id} (service enforces admin-or-owner), DELETE /{id} (ApiAdmin).
  /api/v1/reports (ApiAdmin): GET / (ReportQuery), GET /{id}, POST /, PUT /{id}, DELETE /{id}, POST /{id}/duplicate,
    GET /default/{surveyId} (BuildDefaultAsync — not saved), POST /preview (ReportDefinitionDto → ReportResult),
    GET /{id}/run, GET /{id}/export?format=pdf|csv|txt|xlsx|json (Results.File with ExportFile content type + file name).
  /api/v1/public (anonymous allowed; rate limit "submissions" on POST/PUT): GET /surveys (ListAvailableAsync),
    GET /surveys/{slug} (StartOrResumeAsync), POST /surveys/{surveyId}/responses (SaveResponseRequest → SubmitResponseResult,
    set UserAgent from the request header), PUT /surveys/{surveyId}/draft (SaveDraftAsync; RequireAuthorization(ApiUser) so
    anonymous callers get 401). Verify with an integration test that a bearer-authenticated user's submission gets
    RespondentId set (the default policy scheme makes this work without extra code).
  /api/v1/me (ApiUser): GET /responses (ListMineAsync).
  Branding (IBrandingService): GET /api/v1/public/branding (anonymous; BrandingDto); /api/v1/branding group (ApiAdmin): PUT /
    (UpdateBrandingRequest), POST /logo with JSON body { fileName, contentBase64 } (decode Base64 — invalid ⇒ 400; size/type
    validation is done by the service; JSON instead of multipart avoids antiforgery/CSRF issues for cookie callers), DELETE
    /logo, POST /reset. (The public image endpoints GET /branding/logo and /branding/favicon already exist in
    Infrastructure/BrandingAssets.cs — do not duplicate them; add integration tests for them.)
  /api/v1/dashboard (ApiAdmin): GET / . /api/v1/users (ApiAdmin): GET / (UserQuery), GET /{id}, POST / (CreateUserRequest),
    PUT /{id}/roles (SetUserRolesRequest), POST /{id}/lock, POST /{id}/unlock, DELETE /{id}. /api/v1/audit (ApiAdmin): GET /.
Errors come from ApiExceptionHandler (ProblemDetails) — do not catch AppExceptions in endpoints.

docs/examples/: smartsurvey.http (VS Code REST Client / Visual Studio .http file with variables @baseUrl, login via
POST /api/auth/login {email,password} capturing accessToken, then examples of EVERY endpoint group), create-survey.json (a
realistic survey with 2 sections, radio/checkbox with "Other" free text, rating, and a logic rule referencing client-generated
GUIDs), submit-response.json (matching answers incl. free text), create-report.json (widgets: SummaryStats, PieChart,
CrossTab, TextResponses + an answer filter), and README.md explaining the examples (curl equivalents, auth flow, error format).
Make sure the JSON examples are VALID for the real services (your integration tests should load and post these exact files).

Integration tests (tests/SmartSurvey.IntegrationTests): a WebApplicationFactory<Program> fixture that configures
Database:Provider=Sqlite with a unique shared-cache in-memory connection string (keep one SqliteConnection open for the
fixture lifetime), Seed:AdminPassword, Seed:DemoData=false (plus a separate fixture/collection with DemoData=true for a
couple of read tests), Swagger:Enabled=true, Https:Redirect=false; helper to obtain bearer tokens via /api/auth/login for the
admin and for a newly registered user (POST /api/auth/register). Tests: 401 without auth / 403 for non-admin on admin groups;
full survey lifecycle (create from docs/examples/create-survey.json → get → update with version → conflict on stale version →
publish → duplicate → export definition → import → delete); public flow (anonymous GET session → submit valid → submit
invalid ⇒ 400 ValidationProblem keyed by question id; login-required survey ⇒ anonymous 403/401 behaviour; bearer user
submission records RespondentId; draft save requires auth); responses list/detail/delete/export (csv/xlsx/json content types);
reports create from docs/examples/create-report.json → run → preview → export in all 5 formats (content-type, non-empty,
PDF magic bytes) → default report → duplicate → delete; dashboard; users create/roles/lock/unlock/delete incl. self-protection
rules; audit list contains entries; /health 200; /swagger/v1/swagger.json 200 and lists the endpoints; unknown export format ⇒
400; ProblemDetails shape for 404. Use xUnit collection fixtures to share hosts; keep the suite fast (< ~60 s).
Smoke paths: --user anon /swagger/index.html /health.`,
  },
  {
    key: '6a-public',
    folder: 'Public',
    port: 5202,
    spec: `
TRACK 6A — Public site, respondent hub and account pages.
OWN: Components/Pages/Home.razor (replace), Components/Pages/Faq.razor (new, route /faq), Components/Pages/BuyMeACoffee.razor
(new, route /buy-me-a-coffee), Components/Pages/Public/** (new: AvailableSurveys.razor route /surveys, MyResponses.razor route
/my/responses), Components/Public/** (new shared components for these pages), Components/Account/** (restyle and small logic
changes listed below), tests/SmartSurvey.UnitTests/Web/Public/**.

- Home (/): static SSR marketing landing page that looks like a modern SaaS product: .hero with eyebrow badge, big headline
  with .gradient-text accent, lead text, primary CTA (Browse surveys / Go to dashboard for admins / Create account for guests —
  use AuthorizeView) and secondary CTA; a product "preview" visual built with HTML/CSS (e.g. a mock survey question card and a
  mini chart using ChartView with static ChartData) — no external images; feature grid (6–9 features with .feature-icon: dynamic
  question types, "Other" free text, conditional logic, multi-page + save & resume, anonymous links & QR codes, report builder
  with charts, PDF/CSV/TXT/XLSX exports, dashboard & audit log, REST API); "How it works" 3-step section; a stats/trust strip; FAQ
  teaser linking to /faq; final CTA band; everything responsive.
- FAQ (/faq): static page with searchable accordion (search can be a simple interactive filter — make the page InteractiveServer
  or implement filtering without interactivity via <details>; choose the best UX) of ≥16 accurate Q&As grouped in categories
  (Getting started, Creating surveys, Question types & logic, Responding, Reports & exports, Accounts & privacy, API &
  self-hosting). Answers must be correct for THIS app (read the code/plan: roles, anonymous surveys, drafts, quotas, exports,
  QuestPDF community license note, API auth via /api/auth/login bearer tokens, Docker). Include "Still need help?" card linking to
  Buy me a coffee / contact (SupportOptions.ContactEmail when set).
- Buy Me a Coffee (/buy-me-a-coffee): warm, attractive support page using .coffee-card and .btn-coffee: headline, short story of
  why support matters, the official-style yellow button linking to SupportOptions.BuyMeACoffeeUrl (target _blank, rel noopener),
  a QR code of that URL generated server-side with QRCoder (SvgQRCode → render the SVG markup; QRCoder is referenced by the Web
  project; the SVG is generated from a trusted URL so MarkupString is acceptable here), suggested amounts/tiers as cards (e.g.
  ☕ 1 coffee, ☕☕ 3 coffees, 🍰 coffee + cake) that link to the same URL, other ways to help (star on GitHub when GitHubUrl set,
  share, report bugs), and a thank-you note. Bootstrap Icons over emoji where possible.
- Available surveys (/surveys): InteractiveServer page listing IResponseService.ListAvailableAsync as attractive cards (title,
  description clamp, question count, estimated minutes, closes-at countdown text, badges "Anonymous", "Completed", "Draft saved"),
  search box, CTA "Start"/"Continue"/"View again" linking to /s/{slug}; guests see a banner inviting them to log in to see more
  surveys; EmptyState when none.
- My responses (/my/responses): [Authorize]; list ListMineAsync with StatusBadge, submitted/started times, Continue button for
  drafts (/s/{slug}), EmptyState with link to /surveys.
- Branding: Home hero, FAQ answers, Buy-me-a-coffee copy and auth pages must use the configured product name/tagline from
  IBrandingService (e.g. "Why use {ProductName}?"), never a hard-coded "SmartSurvey".
- Account pages restyle (Components/Account/**): make AccountLayout render a centered auth card (.auth-card) with
  <BrandMark Size="lg" /> and a friendly subtitle; restyle Login (email/password with icons in input-groups, remember me, forgot password, register link,
  Development-only demo credentials hint box: admin@smartsurvey.local / Admin123! and user@smartsurvey.local / User123! — inject
  IWebHostEnvironment and show only when IsDevelopment()), Register (add DisplayName field (optional, ≤200) persisted to
  ApplicationUser.DisplayName; after successful creation add the user to AppRoles.User via UserManager.AddToRoleAsync), and the
  Manage area (ManageLayout/ManageNavMenu as a modern settings layout with a left nav list and a card body). Security logic
  changes: Login must use lockoutOnFailure: true and set ApplicationUser.LastLoginAt = UtcNow on success (via UserManager);
  keep all other Identity behaviour intact. Remove ExternalLoginPicker usage when no external providers (keep file).
  Keep the other Account pages functional; lightly align their markup with the card style where simple.
- Tests: bUnit for AvailableSurveys (cards, flags, empty state) and MyResponses; FAQ contains categories; BuyMeACoffee renders a
  link to the configured URL and an <svg> QR code (provide IOptions<SupportOptions> via Options.Create).
Smoke paths: --user anon / /faq /buy-me-a-coffee /surveys /Account/Login /Account/Register ; --user user /my/responses /surveys
/Account/Manage.`,
  },
  {
    key: '6b-builder',
    folder: 'Builder',
    port: 5203,
    spec: `
TRACK 6B — Admin survey management and the survey BUILDER (the heart of the admin UX).
OWN: Components/Pages/Admin/Surveys/** (new: Index.razor route /admin/surveys, New.razor route /admin/surveys/new,
Edit.razor route /admin/surveys/{Id:guid}/edit, Share.razor route /admin/surveys/{Id:guid}/share), Components/Builder/** (new
builder components), tests/SmartSurvey.UnitTests/Web/Builder/**.
Services: ISurveyService (all), IResponseService only for counts if needed, BrowserInterop (copy, download), ToastService.

- Surveys list (/admin/surveys, InteractiveServer): PageHeader with actions (New survey, Import); tabs or segmented filter: All /
  Drafts / Published / Closed / Archived / Templates; search with debounce; responsive table (or card grid on mobile) showing
  title (+slug), StatusBadge, questions, responses (completed / drafts), last response (relative), updated; row action dropdown:
  Edit, Preview (/admin/surveys/{id}/preview), Share, Responses (/admin/surveys/{id}/responses), Reports (/admin/reports?surveyId=),
  Duplicate, Save as template, Export JSON (download SurveyExportDocument as {slug}.json via BrowserInterop), Publish/Close/
  Reopen/Archive/Restore (status transitions allowed by the service), Delete (ConfirmDialog, danger, mention responses count).
  Pager. EmptyState with CTA. Import modal: InputFile (.json, max 2 MB) → deserialize SurveyExportDocument (System.Text.Json,
  camelCase + string enums, case-insensitive) → ImportDefinitionAsync → toast + navigate to edit; show validation errors.
- New (/admin/surveys/new): a friendly "start" screen: Blank survey card (title input) + template gallery cards (ListTemplatesAsync)
  → CreateAsync (blank with one section) or DuplicateAsync(templateId, {Title}) → navigate to /admin/surveys/{id}/edit.
- Builder (/admin/surveys/{id}/edit, InteractiveServer). Loads SurveyDefinitionDto; edits it IN MEMORY; saves with UpdateAsync
  (send Version; on ConflictException offer "Reload"); dirty tracking with an "Unsaved changes" indicator, Ctrl/⌘+S hint, and
  NavigationLock to confirm leaving with unsaved changes. Layout: PageHeader (title, StatusBadge, actions: Preview, Share,
  Save (busy), Publish/Close dropdown) + .builder-layout: main column with tabs "Questions", "Logic", "Settings"; right
  .builder-aside with survey outline (sections/questions quick-jump), stats (questions, required, rules), validation checklist
  (e.g. "Add at least one question", "Choice questions need 2+ options", logic warnings) and publish readiness.
  * Questions tab: sections as .section-block (editable title/description inline, move up/down, delete with confirm, add
    section). Questions as collapsible .question-editor cards (type icon via UiFormat.QuestionTypeIcon, code chip, text,
    required toggle, duplicate, move up/down within section, move to another section, delete with confirm — call
    CountAnswersAsync and warn "N stored answers will be deleted" when > 0). Adding a question opens a .type-picker with all 10
    QuestionType options (DisplayName + icon). Editor body: text (required), help text, type switcher (converting keeps text,
    adds 2 default options when switching to a choice type), required switch, Options editor for choice types (add, inline
    edit, reorder up/down, delete, per-option "Allows free text" toggle + placeholder — i.e. the combined "Other → text" type —
    and a one-click "Add 'Other (please specify)'" button, optional export value), and type-specific Settings panel
    (placeholder, min/max length, min/max value + allow decimals, min/max selections, rating max (stars preview), scale
    min/max + end labels (live preview of the scale), min/max date, randomize options). New items get Guid.NewGuid() ids so
    logic can reference them before saving.
  * Logic tab: explain semantics briefly (Show = hidden until conditions match; Hide wins; conditions can only reference
    EARLIER questions). List rules grouped by target; "Add rule" wizard: pick target (question or section), action (Show/Hide),
    match type (All/Any), conditions rows: source question (only questions BEFORE the target in display order), operator (from
    SupportedOperators of the source type, DisplayName labels), value editor adapted to the source type (option dropdown for
    choice questions, number input, date input, text input; hidden for unary operators). Human-readable sentence preview:
    "Show Q3 “Why not?” when Q1 “Did you enjoy…” is “No”". Inline warnings for invalid rules.
  * Settings tab: title, description, slug (with availability check via IsSlugAvailableAsync, debounced, and a "generate from
    title" button), welcome & thank-you messages, AllowAnonymous, AllowMultipleResponses, ShowProgressBar, ShowQuestionNumbers,
    IsTemplate, schedule OpensAt/ClosesAt (datetime-local inputs; convert browser-local ⇄ UTC with BrowserInterop
    ToUtcAsync/ToLocalAsync), MaxResponses. Group into cards with helper texts.
  * Map AppValidationException errors (keys like "Sections[0].Questions[1].Text") to a readable error list and highlight the
    affected question cards where feasible.
- Share (/admin/surveys/{id}/share): public link (NavigationManager.BaseUri + "s/{slug}") with copy button (BrowserInterop
  .CopyTextAsync + toast), QR code (QRCoder SvgQRCode of the link, rendered as SVG in .qr-box) with "Download SVG" (via
  DownloadAsync), embed snippet (<iframe> code box with copy), status warnings (draft/closed/not open yet/login required) with a
  Publish button when draft, and access summary (anonymous vs login, one response per user, schedule, quota).
- Keep components focused: e.g. Builder/QuestionEditor.razor, OptionsEditor.razor, QuestionSettingsEditor.razor,
  SectionBlock.razor, TypePicker.razor, LogicTab.razor, LogicRuleEditor.razor, ConditionRow.razor, SurveySettingsForm.razor,
  BuilderAside.razor, plus a small BuilderState/UndoFree helper class if useful (e.g. reorder helpers, "questions before target").
- Tests (bUnit + plain unit): OptionsEditor add/remove/reorder/free-text toggle; TypePicker raises selected type; LogicRuleEditor
  only offers earlier questions and type-appropriate operators/value editors; rule sentence text; switching question type adds
  default options; builder helper functions (reorder, move between sections, questions-before-target). Use a fake ISurveyService.
Smoke paths (admin): /admin/surveys /admin/surveys/new /admin/surveys/{a demo survey id}/edit /admin/surveys/{id}/share.`,
  },
  {
    key: '6c-runner',
    folder: 'Runner',
    port: 5204,
    spec: `
TRACK 6C — Respondent experience: the survey RUNNER, take-survey pages and admin preview.
OWN: Components/Survey/** (new: SurveyRunner.razor, QuestionInput.razor and helpers), Components/Pages/Survey/** (new:
TakeSurvey.razor route /s/{Slug}, ThankYou.razor route /s/{Slug}/thank-you), Components/Pages/Admin/Preview/SurveyPreview.razor
(new, route /admin/surveys/{Id:guid}/preview), tests/SmartSurvey.UnitTests/Web/Runner/**.
Services: IResponseService (StartOrResumeAsync, SaveDraftAsync, SubmitAsync), ISurveyService.GetAsync (preview only),
LogicEvaluator, ResponseValidator, BrowserInterop (scroll to top/error), AuthenticationState.

- SurveyRunner (reusable, InteractiveServer when hosted by an interactive page): parameters SurveyDefinitionDto Survey,
  initial answers, initial page index, bool PreviewMode, bool CanSaveDraft, EventCallbacks OnSaveDraft(SaveResponseRequest) →
  Task<Guid?>, OnSubmit(SaveResponseRequest) → Task (the host maps service calls/exceptions). Behaviour: keeps a
  Dictionary<Guid, AnswerInputDto>; recomputes LogicEvaluator.Evaluate after every change (live branching); pages = visible
  sections only (VisibleSections), recalculated as answers change (keep the current page stable when possible); progress bar
  (Survey.ShowProgressBar) "Page x of y" + percent; question numbering across visible questions (ShowQuestionNumbers); optional
  welcome screen (WelcomeMessage) with Start button; Next validates the current page with ResponseValidator.Validate(…, section
  ids of current page) and shows errors per question (question-card.has-error + field-error), scrolling to the first error;
  Back never validates; Submit validates everything visible, then calls OnSubmit; server-side AppValidationException (keys =
  question ids) must be shown on the right questions (navigate to the page of the first invalid question). Draft auto-save on
  page change when CanSaveDraft (debounced; tiny "Saved just now" indicator) + a "Save & continue later" button. Options
  randomisation (Settings.RandomizeOptions): shuffle once per runner instance with a stable seed; free-text options stay last.
  Keyboard friendly; big touch targets; smooth, calm, professional look (.survey-shell/.survey-hero/.question-card classes).
  Preview mode: banner "Preview — responses are not saved", submit shows a success panel instead of calling services, plus a
  "Restart" button.
- QuestionInput: renders one question per type — ShortText (input, placeholder, maxlength + counter when MaxLength), LongText
  (textarea auto-grow feel, counter), Email (type=email), Number (type=number with min/max/step from AllowDecimals), Date
  (type=date with min/max), Radio (.choice-list of .choice-item labels with radio inputs; .selected state), Checkbox (same with
  checkboxes, "select up to N" hint), Dropdown (<select> with placeholder "Choose…"), free-text input appearing under an
  AllowsFreeText option when selected (placeholder from option), Rating (.rating-stars buttons with aria-labels "3 of 5 stars",
  keyboard accessible, clear option), Scale (.scale-options buttons ScaleMin..ScaleMax + .scale-labels). Unique element ids per
  question for label association; aria-invalid when errors; required asterisk; help text.
- TakeSurvey (/s/{Slug}, InteractiveServer): calls StartOrResumeAsync; handles eligibility states with beautiful EmptyState
  panels and CTAs: NotFound, NotPublished, NotOpenYet (show opening time with LocalDateTime), Closed, QuotaReached,
  LoginRequired (button to /Account/Login?returnUrl=/s/{slug}), AlreadyResponded (link to /my/responses and /surveys). When
  Eligible: survey hero (title, description, estimated time, question count, anonymous note / resume note "Welcome back — we
  restored your saved answers"), SurveyRunner with CanSaveDraft = authenticated; OnSaveDraft → SaveDraftAsync (remember draft
  id); OnSubmit → SubmitAsync(surveyId, request) → navigate to /s/{slug}/thank-you (pass the thank-you message via a scoped
  per-circuit holder service you create in Components/Survey/, or query flag + fetch message; do not put long text in the URL).
  BusinessRuleException (e.g. survey closed meanwhile) shows an alert.
- ThankYou (/s/{Slug}/thank-you): .success-check animation, the survey thank-you message (or a default), buttons: Browse more
  surveys, My responses (when logged in), Home.
- SurveyPreview (/admin/surveys/{Id}/preview, admin, InteractiveServer): loads GetAsync; toolbar with "Back to builder", device
  width toggle (desktop / tablet / mobile container widths), status badge; SurveyRunner in PreviewMode.
- Tests (bUnit): QuestionInput renders the right control for each of the 10 types and updates the AnswerInputDto (text input,
  radio select, checkbox toggle, free-text shows only when "Other" selected, rating click, scale click); SurveyRunner: hidden
  question appears after answering the controlling question (use SampleSurveys.CustomerFeedback), Next blocked with required
  error, page navigation, submit calls OnSubmit with only visible answers, server validation errors are displayed, preview mode
  never calls callbacks for saving. Use a fake IResponseService where needed.
Smoke paths: --user anon /s/{slug of the published anonymous demo survey} /s/does-not-exist /s/{slug}/thank-you ; --user user
/s/{slug of the login-required engagement survey} ; --user admin /admin/surveys/{id}/preview.`,
  },
  {
    key: '6d-reports',
    folder: 'Reports',
    port: 5205,
    spec: `
TRACK 6D — Admin reporting UI: report list, dynamic report BUILDER with live preview, report viewer and exports.
OWN: Components/Pages/Admin/Reports/** (new: Index.razor route /admin/reports (supports ?surveyId=), Builder.razor routes
/admin/reports/new (query surveyId, template=default|blank) and /admin/reports/{Id:guid}/edit, View.razor route
/admin/reports/{Id:guid}), Components/Reports/** (new), tests/SmartSurvey.UnitTests/Web/Reports/**.
Services: IReportService (all), ISurveyService (ListAsync / GetAsync for question pickers), BrowserInterop (DownloadAsync),
ToastService, ChartView (shared).

- Reports list: PageHeader + "New report" button opening a Modal: choose survey (searchable select of non-template surveys),
  start from "Recommended (auto-generated)" (BuildDefaultAsync) or "Blank"; table/cards of reports (name, survey, widget count,
  updated, actions: View, Edit, Duplicate, Export ▸ (PDF/CSV/TXT/XLSX/JSON), Delete with confirm); survey filter (from query
  string) and search; pager; EmptyState with CTA.
- Builder: InteractiveServer. Two-pane layout (settings/widgets editor left ~40%, live preview right ~60%; stacked on mobile):
  * Report settings card: name, description, survey (fixed after creation; for new reports selectable), Filters: date range
    (From/To date inputs), include in-progress switch, answer filters (rows: question → operator (SupportedOperators) → value
    editor adapted to type — option select for choice questions, number/date/text input; All/Any toggle).
  * Widgets list: "Add widget" type picker (all WidgetType values with icons from UiFormat.WidgetTypeIcon + DisplayName +
    one-line descriptions); each widget as a collapsible card: type (switchable), title (placeholder = question text), primary
    question select filtered by compatibility (choice/numeric for charts; choice-only for CrossTab rows/columns; any for tables;
    text/choice-with-free-text for TextResponses; hidden when not required), secondary question for CrossTab, settings (show
    percentages, sort order, top N, time grouping for LineChart, include free text, show data table, max rows, columns multi-
    select for RawResponses), move up/down, duplicate, delete.
  * Live preview: debounce edits (~600 ms) → PreviewAsync(dto) → render with ReportResultView; show spinner overlay while
    refreshing; show validation problems (AppValidationException) inline instead of the preview.
  * Save (CreateAsync / UpdateAsync) with busy state, toast, dirty indicator, NavigationLock; after first save navigate to edit URL.
- ReportResultView / WidgetView (Components/Reports): header (report name, survey, generated time, responses count, filter
  summary chips), then widgets in a responsive grid (KPI/summary & charts in cards; wide tables full-width): StatItems as .kpi-grid,
  ChartView for Chart data, TableData rendered as styled tables (numeric columns right-aligned; a percentage column shows an
  inline .bar-inline; footer row bold), ExtraTables below with captions, Note (muted, info icon), Error (warning alert). Handle
  empty results gracefully ("No responses match the filters").
- Viewer (/admin/reports/{id}): RunAsync on load and on "Refresh"; PageHeader actions: Edit, Export dropdown (PDF, CSV, TXT, Excel,
  JSON) → ExportAsync → BrowserInterop.DownloadAsync (busy state per format; toast on success/error), Print (window.print via
  JS eval is NOT allowed — use a link/button with onclick="window.print()" attribute in markup, that is fine).
- Tests (bUnit): WidgetView renders stats, table (numeric alignment class, footer), extra tables, note and error; ReportResultView
  empty state; builder widget editor shows secondary question only for CrossTab and filters question options by compatibility;
  filter row value editor switches with question type. Use fakes for IReportService/ISurveyService.
Smoke paths (admin): /admin/reports /admin/reports/new?surveyId={demo survey id}&template=default /admin/reports/{demo report
id} /admin/reports/{demo report id}/edit.`,
  },
  {
    key: '6e-admin',
    folder: 'Admin',
    port: 5206,
    spec: `
TRACK 6E — Admin dashboard, responses browser, response detail, user management and audit log.
OWN: Components/Pages/Admin/Dashboard.razor (new, route /admin), Components/Pages/Admin/Responses/** (new: SurveyResponses.razor
route /admin/surveys/{SurveyId:guid}/responses, ResponseDetail.razor route /admin/responses/{Id:guid}),
Components/Pages/Admin/Users/** (new: Index.razor route /admin/users), Components/Pages/Admin/Audit/** (new: Index.razor route
/admin/audit), Components/Pages/Admin/Branding/** (new: Index.razor route /admin/branding — the sidebar already links to it),
Components/AdminPanels/** (new shared components for these pages), tests/SmartSurvey.UnitTests/Web/Admin/**.
Services: IDashboardService, IResponseService (ListForSurveyAsync, GetAsync, DeleteAsync), IResponseExportService,
ISurveyService.GetAsync (survey title/status for headers), IUserAdminService, IAuditService, BrowserInterop, ToastService,
ChartView, StatCard.

- Dashboard (/admin, InteractiveServer): greeting ("Good morning, {name}") + date; KPI row of StatCards (Published surveys,
  Completed responses (+last 7 days hint), Completion rate, Avg completion time, Users, Reports); "Responses — last 30 days"
  card with ChartView (Line or Bar from ResponsesPerDay, labels "d MMM"); Top surveys card (table: title → edit link, status,
  responses, mini .bar-inline share); Recent responses list (survey, respondent, relative time → link to detail); Quick actions
  card (New survey, New report, Manage users, View audit log); survey status breakdown (Published/Draft/Closed as small donut via
  ChartView or badges). Skeleton placeholders while loading. EmptyState-friendly when there is no data yet ("Create your first
  survey").
- Survey responses (/admin/surveys/{SurveyId}/responses): header with survey title + StatusBadge + actions (Build report →
  /admin/reports/new?surveyId=…&template=default, Export ▸ CSV / Excel / JSON with "include drafts" toggle via
  IResponseExportService + DownloadAsync); filters: status (All/Completed/In progress), date range, search respondent; table:
  respondent (avatar initials + name/email or "Anonymous"), status, started, submitted, duration (UiFormat.Duration), answers
  count, actions (View, Delete with confirm); Pager; EmptyState (with share link hint).
- Response detail (/admin/responses/{Id}): header (survey title link, respondent, status badge, submitted, duration, user agent in
  muted small text), answers grouped by section in cards: question code chip + text, answer DisplayValue (unanswered shown as
  muted "— not answered"/"skipped"), actions: Back to responses, Delete (confirm → navigate back), Print.
- Users (/admin/users): search + role filter; table (avatar, name, email, roles as badges, status Active/Locked, email
  confirmed, registered, last login, responses); actions: Edit roles (Modal with Admin/User switches; explain self-protection
  rules; surface BusinessRuleException messages), Lock/Unlock (confirm), Delete (danger confirm). "Invite/Create user" Modal (email,
  display name, password with show/hide + strength hint, roles) → CreateAsync; show AppValidationException messages inline.
  Current admin's own row shows a "You" badge and disables self-destructive actions.
- Audit log (/admin/audit): filters (search, entity type select (Survey/Response/Report/User), date range), table (time with
  LocalDateTime + relative, user, action rendered as a friendly label + colored badge by verb (created/updated/deleted/...),
  entity type + id (link to entity where sensible: surveys → edit page), details), Pager; EmptyState.
- Branding (/admin/branding, InteractiveServer) — lets admins customise the product's title and icon (user request):
  IBrandingService (GetAsync, UpdateAsync, SetLogoAsync(bytes, fileName), RemoveLogoAsync, ResetAsync) + BrandingDefaults
  (SuggestedIcons, MaxLogoBytes, length limits). Layout: left card "Identity" — Product name (required, max 80, live character
  counter), Tagline (max 200); "Brand icon" card — segmented choice "Built-in icon" vs "Upload logo": icon picker grid (.icon-picker,
  buttons with the suggested Bootstrap icons, aria-label per icon, selected state, plus a free-text field accepting any valid
  "bi-…" class with live validation) and logo upload (InputFile accept=".png,.jpg,.jpeg,.gif,.webp,.ico,.svg", max 512 KB, shows
  current logo preview from LogoUrl, Remove logo with confirm; surface AppValidationException messages such as unsupported type /
  too large); right column sticky "Live preview" card showing how the public navbar, the admin sidebar brand (dark background)
  and the browser tab (favicon + "Page · {name}") will look with the UNSAVED values (render the preview locally — for an
  unsaved uploaded image use a data: URL built from the uploaded bytes after validating the file type is an image; never render
  uploaded SVG inline as markup — use <img src>). Actions: Save (busy, toast "Branding updated — refresh other tabs to see
  it"), Reset to defaults (confirm), dirty indicator + NavigationLock. After saving, call NavigationManager.Refresh(forceReload:
  true) or instruct to reload so the static layout picks up the new brand (explain in a small hint).
- Tests (bUnit): Dashboard renders KPI values and empty state; SurveyResponses renders rows, filter changes call the service with
  the right query, delete confirm flow; Users page disables self actions and shows create validation errors; Audit renders
  badges by action verb; Branding page validates name, selects an icon, calls UpdateAsync with the chosen values, shows logo
  upload errors from the service. Use fakes.
Smoke paths (admin): /admin /admin/surveys/{demo survey id}/responses /admin/responses/{demo response id} /admin/users
/admin/audit /admin/branding.`,
  },
]

const REVIEW_FIX = (t, impl) => `${common(t)}
${t.spec}

YOU ARE THE INDEPENDENT REVIEWER-AND-FIXER for track ${t.key}. A different engineer implemented it; you did not write this code.
Your worktree ${wt(t)} contains their commit ${impl.commitSha} (check: \`cd "${wt(t)}" && git log --oneline -3\`; if HEAD is not that
commit run \`cd "${wt(t)}" && git reset --hard ${impl.commitSha}\`). Implementer summary: ${impl.summary}
Implementer smoke result: ${impl.smokeResult || 'n/a'} · Open issues they reported: ${JSON.stringify(impl.openIssues)}

STEP 1 — ADVERSARIAL REVIEW. Review everything in \`git diff ${BASE} HEAD\` against the spec above and the general rules. Hunt for
REAL problems: missing spec features; broken behaviour (wrong service calls, unhandled exceptions, lost state, prerender
double-execution side effects, JS interop during prerender, missing @rendermode on interactive pages, handlers that do not
re-render, disposal leaks, races in debounced code, NavigationLock misuse); authorization gaps (admin pages outside the Admin
folder, endpoints missing policies, anonymous access where it should not be); security (MarkupString with user data, XSS, open
redirects via returnUrl, CSRF on state-changing endpoints, info leaks); API/HTTP semantics (status codes, Location headers,
ProblemDetails, file results, rate limits); UX below the professional standard (missing loading/empty/error states, no
confirmation for destructive actions, inaccessible controls, broken responsive layout, inconsistent styling, raw enum names or
unformatted dates/numbers shown to users); file-ownership violations; weak tests.
STEP 2 — FIX every real critical/major issue and cheap minor ones; add/adjust tests. Re-run build (0 warnings), ALL tests
(green) and the smoke test for the track's pages. Commit "Phase <5|6x> review fixes: <summary>" with the Co-Authored-By trailer
(if nothing needed fixing, do not create an empty commit — report the existing HEAD).
Report: commitSha (HEAD after your work), summary listing every finding as "[severity] title — fixed | rejected(reason)",
files changed, tests added, total tests passing, smoke result, remaining open issues.`

phase('Implement')
const results = await pipeline(
  TRACKS,
  (t) => agent(`${common(t)}\n${t.spec}\n\nWhen finished, return the structured report.`, {
    label: `impl:${t.key}`,
    phase: 'Implement',
    schema: IMPL_SCHEMA,
  }),
  (impl, t) => {
    if (!impl || !impl.commitSha) return { key: t.key, final: null, impl }
    return agent(REVIEW_FIX(t, impl), {
      label: `review+fix:${t.key}`,
      phase: 'Review',
      schema: IMPL_SCHEMA,
    }).then((fix) => ({ key: t.key, final: fix?.commitSha || impl.commitSha, impl, fix }))
  },
)

return results.map((r, i) => r ? r : { key: TRACKS[i].key, final: null })
