using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Logic;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Responses;

/// <summary>
/// Response collection use cases: the respondent flow (available surveys, start/resume, drafts,
/// submission) and the admin flow (browse, inspect, delete).
/// </summary>
/// <remarks>
/// The server never trusts the client: drafts and submissions are sanitised against the stored survey
/// design, and submissions re-run conditional logic (<see cref="LogicEvaluator"/>) and validation
/// (<see cref="ResponseValidator"/>) with exactly the code the Blazor runner uses. Every operation
/// works on its own short-lived context, which keeps the service safe for long-lived Blazor circuits.
/// </remarks>
/// <param name="dbFactory">Creates one context per operation.</param>
/// <param name="currentUser">The acting user.</param>
/// <param name="time">Clock (all timestamps are UTC).</param>
/// <param name="audit">Audit trail.</param>
/// <param name="requestValidator">Structural validation of draft/submission payloads.</param>
/// <param name="queryValidator">Validation of the admin list filters.</param>
/// <param name="accessKeys">Access keys of password-protected surveys.</param>
/// <param name="logger">Logger.</param>
public sealed class ResponseService(
    IAppDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider time,
    IAuditService audit,
    IValidator<SaveResponseRequest> requestValidator,
    IValidator<ResponseQuery> queryValidator,
    ISurveyAccessKeys accessKeys,
    ILogger<ResponseService> logger) : IResponseService
{
    /// <summary>Maximum stored user-agent length (matches the database column).</summary>
    public const int MaxUserAgentLength = 512;

    /// <summary>Assumed answering time per question used for <see cref="AvailableSurveyDto.EstimatedMinutes"/>.</summary>
    public const int SecondsPerQuestion = 20;

    private const string AnonymousName = "Anonymous";
    private const string PasswordRequiredMessage = "This survey is protected with a password. Please enter the password to continue.";
    private const string ResponseEntity = "Response";
    private const string SurveyEntity = "Survey";

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    // Only the id of an authenticated principal is trusted.
    private Guid? UserId => currentUser.IsAuthenticated ? currentUser.UserId : null;

    // ---------------------------------------------------------------- respondent flow

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableSurveyDto>> ListAvailableAsync(CancellationToken ct = default)
    {
        var now = UtcNow;
        var userId = UserId;

        // Guid.Empty never matches a respondent, so guests get "false" flags from the same query shape.
        var userKey = userId ?? Guid.Empty;

        await using var db = await dbFactory.CreateAsync(ct);

        // Status and schedule are pre-filtered in SQL; the eligibility checker below then applies the
        // complete rule set (quota, one response per user) exactly like StartOrResumeAsync does.
        // Password-protected surveys are for people who received the link and password: never listed publicly.
        var candidates = db.Surveys.AsNoTracking()
            .Where(s => s.Status == SurveyStatus.Published && !s.IsTemplate && s.AccessPasswordHash == null)
            .Where(s => (s.OpensAt == null || s.OpensAt <= now) && (s.ClosesAt == null || s.ClosesAt > now));
        if (userId is null)
        {
            candidates = candidates.Where(s => s.AllowAnonymous);
        }

        // A single round trip: counts and per-user flags are correlated sub-queries (no N+1).
        var rows = await candidates
            .Select(s => new AvailableSurveyRow(
                s,
                s.Questions.Count,
                s.Responses.Count(r => r.Status == ResponseStatus.Completed),
                s.Responses.Any(r => r.RespondentId == userKey && r.Status == ResponseStatus.Completed),
                s.Responses.Any(r => r.RespondentId == userKey && r.Status == ResponseStatus.InProgress)))
            .ToListAsync(ct);

        var isAuthenticated = userId.HasValue;
        return rows
            .Select(row => (Row: row, Verdict: SurveyEligibilityChecker.Check(
                row.Survey, now, new ParticipationFacts(row.CompletedCount, isAuthenticated, row.UserCompleted))))
            // Surveys the user already completed stay on the list (flagged) so they know about them.
            .Where(x => x.Verdict.Eligibility is SurveyEligibility.Eligible or SurveyEligibility.AlreadyResponded)
            // Sorted in memory: NULL ordering differs between PostgreSQL and SQLite.
            .OrderByDescending(x => x.Row.Survey.PublishedAt ?? x.Row.Survey.CreatedAt)
            .ThenBy(x => x.Row.Survey.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => ToAvailableDto(x.Row, x.Verdict))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SurveySessionDto> StartOrResumeAsync(string slug, string? accessKey = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return ToSession(SurveyEligibilityChecker.NotFound);
        }

        var normalizedSlug = slug.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateAsync(ct);

        var survey = await db.LoadSurveyGraphAsync(s => s.Slug.ToLower() == normalizedSlug, ct);
        var verdict = survey is null
            ? SurveyEligibilityChecker.NotFound
            : await CheckEligibilityAsync(db, survey, ct);

        var session = ToSession(verdict);
        if (!verdict.IsEligible || survey is null)
        {
            return session; // the design is only handed out to eligible respondents
        }

        if (survey.AccessPasswordHash is { } passwordHash && !accessKeys.IsValid(accessKey, survey.Id, passwordHash))
        {
            session.Eligibility = SurveyEligibility.PasswordRequired;
            session.Message = PasswordRequiredMessage;
            session.SurveyTitle = survey.Title;
            return session;
        }

        session.SurveyTitle = survey.Title;

        var definition = survey.ToDefinitionDto();
        session.Survey = definition;
        if (UserId is { } userId)
        {
            await AttachLatestDraftAsync(db, session, definition, userId, ct);
        }

        return session;
    }

    /// <inheritdoc />
    public async Task<SurveyUnlockResult> UnlockAsync(string slug, string password, CancellationToken ct = default)
    {
        var normalizedSlug = slug?.Trim().ToLowerInvariant() ?? string.Empty;
        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await db.Surveys.AsNoTracking()
            .Where(s => s.Slug.ToLower() == normalizedSlug && !s.IsTemplate && s.Status != SurveyStatus.Draft)
            .Select(s => new { s.Id, s.AccessPasswordHash })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(SurveyEntity, slug ?? string.Empty);

        if (survey.AccessPasswordHash is null)
        {
            // Nothing to unlock; any key works for an unprotected survey.
            return new SurveyUnlockResult(string.Empty, UtcNow.Add(accessKeys.Lifetime));
        }

        if (!SurveyPasswordHasher.Verify(password, survey.AccessPasswordHash))
        {
            logger.LogInformation("Wrong password entered for survey {SurveyId}.", survey.Id);
            throw new AppValidationException("Password", "That password is not correct. Please check it and try again.");
        }

        return new SurveyUnlockResult(accessKeys.Issue(survey.Id, survey.AccessPasswordHash), UtcNow.Add(accessKeys.Lifetime));
    }

    /// <inheritdoc />
    public async Task<Guid> SaveDraftAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = UserId ?? throw new ForbiddenException("Please log in to save your progress.");
        await ValidateAsync(requestValidator, request, ct);

        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await LoadEligibleSurveyAsync(db, surveyId, request.AccessKey, ct);
        var definition = survey.ToDefinitionDto();
        var questions = IndexQuestions(definition);

        // Drafts skip required/format validation (the respondent is not finished) but are always sanitised.
        var answers = ResponseAnswerWriter.Sanitize(questions, request.Answers);
        var draft = await FindDraftForUpdateAsync(db, surveyId, userId, request.ResponseId, ct)
            ?? StartResponse(db, surveyId, userId);

        ResponseAnswerWriter.Replace(db, draft, answers, questions);
        draft.CurrentSectionIndex = ClampSectionIndex(request.CurrentSectionIndex, definition.Sections.Count);
        draft.UpdatedAt = UtcNow;
        draft.UserAgent = TruncateUserAgent(request.UserAgent) ?? draft.UserAgent;

        await db.SaveChangesAsync(ct);
        logger.LogDebug("Draft {ResponseId} of survey {SurveyId} saved with {AnswerCount} answers.", draft.Id, surveyId, answers.Count);
        return draft.Id;
    }

    /// <inheritdoc />
    public async Task<SubmitResponseResult> SubmitAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ValidateAsync(requestValidator, request, ct);

        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await LoadEligibleSurveyAsync(db, surveyId, request.AccessKey, ct);
        var definition = survey.ToDefinitionDto();
        var questions = IndexQuestions(definition);
        var answers = SanitizeAndValidateSubmission(definition, questions, request.Answers);

        // Logged-in respondents complete their draft (keeping its StartedAt); everyone else starts fresh.
        // Guests cannot own drafts, so a ResponseId sent by a guest is ignored.
        var userId = UserId;
        var response = (userId is { } uid ? await FindDraftForUpdateAsync(db, surveyId, uid, request.ResponseId, ct) : null)
            ?? StartResponse(db, surveyId, userId);

        ResponseAnswerWriter.Replace(db, response, answers, questions);
        var now = UtcNow;
        response.Status = ResponseStatus.Completed;
        response.SubmittedAt = now;
        response.UpdatedAt = now;
        response.UserAgent = TruncateUserAgent(request.UserAgent) ?? response.UserAgent;

        await EnsureQuotaAvailableAsync(db, survey, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Response {ResponseId} submitted for survey {SurveyId}.", response.Id, survey.Id);
        await audit.LogAsync(
            AuditActions.ResponseSubmitted,
            ResponseEntity,
            response.Id.ToString(),
            $"Response to survey \"{survey.Title}\" ({survey.Id}) submitted with {answers.Count} answers.",
            ct);

        return new SubmitResponseResult(response.Id, survey.ThankYouMessage);
    }

    /// <inheritdoc />
    public async Task<SurveyCompletionDto?> GetCompletionAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalizedSlug = slug.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(s => s.Slug.ToLower() == normalizedSlug, ct);
        // Password-protected surveys show their thank-you message inside the survey page instead (this page is public).
        if (survey is null || survey.IsTemplate || survey.Status == SurveyStatus.Draft || survey.AccessPasswordHash is not null
            || (!survey.AllowAnonymous && UserId is null))
        {
            return null;
        }

        // "Answer again" is only offered where repeat answers are intended; guests cannot be told apart,
        // so without AllowMultipleResponses the button would just invite duplicates.
        var verdict = await CheckEligibilityAsync(db, survey, ct);
        return new SurveyCompletionDto(
            survey.Id, survey.Title, survey.Slug, survey.ThankYouMessage, verdict.IsEligible && survey.AllowMultipleResponses);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MyResponseDto>> ListMineAsync(CancellationToken ct = default)
    {
        if (UserId is not { } userId)
        {
            return [];
        }

        var now = UtcNow;
        await using var db = await dbFactory.CreateAsync(ct);
        var rows = await db.Responses.AsNoTracking()
            .Where(r => r.RespondentId == userId)
            .Select(r => new
            {
                r.Id,
                r.SurveyId,
                r.Survey!.Title,
                r.Survey.Slug,
                r.Status,
                r.StartedAt,
                r.UpdatedAt,
                r.SubmittedAt,
                SurveyStatus = r.Survey.Status,
                r.Survey.IsTemplate,
                r.Survey.OpensAt,
                r.Survey.ClosesAt,
            })
            .ToListAsync(ct);

        return rows
            .OrderByDescending(r => r.SubmittedAt ?? r.UpdatedAt ?? r.StartedAt)
            .Select(r => new MyResponseDto
            {
                ResponseId = r.Id,
                SurveyId = r.SurveyId,
                SurveyTitle = r.Title,
                Slug = r.Slug,
                Status = r.Status,
                StartedAt = r.StartedAt,
                SubmittedAt = r.SubmittedAt,
                CanContinue = r.Status == ResponseStatus.InProgress
                    && SurveyEligibilityChecker.GetAvailability(r.SurveyStatus, r.IsTemplate, r.OpensAt, r.ClosesAt, now)
                        == SurveyAvailability.Open,
            })
            .ToList();
    }

    // ---------------------------------------------------------------- admin flow

    /// <inheritdoc />
    public async Task<PagedResult<ResponseSummaryDto>> ListForSurveyAsync(Guid surveyId, ResponseQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureAdmin("browse responses");
        await ValidateAsync(queryValidator, query, ct);

        await using var db = await dbFactory.CreateAsync(ct);
        if (!await db.Surveys.AnyAsync(s => s.Id == surveyId, ct))
        {
            throw new NotFoundException(SurveyEntity, surveyId);
        }

        var filtered = ApplyFilters(db.Responses.AsNoTracking().Where(r => r.SurveyId == surveyId), query);
        var total = await filtered.CountAsync(ct);
        var rows = await filtered
            .OrderByDescending(r => r.SubmittedAt ?? r.StartedAt)
            .ThenByDescending(r => r.StartedAt)
            .ThenBy(r => r.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(r => new
            {
                r.Id,
                r.SurveyId,
                r.RespondentId,
                r.Respondent!.DisplayName,
                r.Respondent.Email,
                r.Status,
                r.StartedAt,
                r.SubmittedAt,
                AnswerCount = r.Answers.Count,
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new ResponseSummaryDto
            {
                Id = r.Id,
                SurveyId = r.SurveyId,
                RespondentId = r.RespondentId,
                RespondentName = RespondentName(r.DisplayName, r.Email),
                RespondentEmail = r.Email,
                Status = r.Status,
                StartedAt = r.StartedAt,
                SubmittedAt = r.SubmittedAt,
                AnswerCount = r.AnswerCount,
                DurationSeconds = DurationSeconds(r.Status, r.StartedAt, r.SubmittedAt),
            })
            .ToList();

        return new PagedResult<ResponseSummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<ResponseDetailDto> GetAsync(Guid responseId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateAsync(ct);
        var response = await db.LoadResponseWithAnswersAsync(q => q.Where(r => r.Id == responseId), tracked: false, ct)
            ?? throw new NotFoundException(ResponseEntity, responseId);

        var isOwner = UserId is { } userId && response.RespondentId == userId;
        if (!currentUser.IsAdmin && !isOwner)
        {
            throw new ForbiddenException("You can only view your own responses.");
        }

        var survey = await db.LoadSurveyGraphAsync(s => s.Id == response.SurveyId, ct)
            ?? throw new NotFoundException(SurveyEntity, response.SurveyId);

        // Project only the profile fields needed (never materialise credentials).
        var respondent = response.RespondentId is { } respondentId
            ? await db.Users.AsNoTracking()
                .Where(u => u.Id == respondentId)
                .Select(u => new { u.DisplayName, u.Email })
                .FirstOrDefaultAsync(ct)
            : null;

        return new ResponseDetailDto
        {
            Id = response.Id,
            SurveyId = response.SurveyId,
            SurveyTitle = survey.Title,
            RespondentId = response.RespondentId,
            RespondentName = RespondentName(respondent?.DisplayName, respondent?.Email),
            RespondentEmail = respondent?.Email,
            Status = response.Status,
            StartedAt = response.StartedAt,
            SubmittedAt = response.SubmittedAt,
            UserAgent = response.UserAgent,
            Answers = BuildAnswerDetails(survey.ToDefinitionDto(), response.Answers),
        };
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid responseId, CancellationToken ct = default)
    {
        EnsureAdmin("delete responses");

        await using var db = await dbFactory.CreateAsync(ct);
        var response = await db.Responses.FirstOrDefaultAsync(r => r.Id == responseId, ct)
            ?? throw new NotFoundException(ResponseEntity, responseId);

        db.Responses.Remove(response); // answers and selections follow via the database cascade
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            AuditActions.ResponseDeleted,
            ResponseEntity,
            responseId.ToString(),
            $"{response.Status} response to survey {response.SurveyId} deleted.",
            ct);
    }

    // ---------------------------------------------------------------- eligibility & loading

    /// <summary>Gathers the response facts for <paramref name="survey"/> and evaluates eligibility.</summary>
    private async Task<EligibilityVerdict> CheckEligibilityAsync(IAppDbContext db, Survey survey, CancellationToken ct)
    {
        var completed = await db.CountCompletedAsync(survey.Id, ct);
        var userId = UserId;
        var hasCompleted = userId.HasValue && await db.Responses.AnyAsync(
            r => r.SurveyId == survey.Id && r.RespondentId == userId && r.Status == ResponseStatus.Completed, ct);

        return SurveyEligibilityChecker.Check(survey, UtcNow, new ParticipationFacts(completed, userId.HasValue, hasCompleted));
    }

    /// <summary>
    /// Loads the survey graph and throws unless the current user may respond:
    /// <see cref="NotFoundException"/> for unknown surveys, <see cref="ForbiddenException"/> when a login
    /// is required and <see cref="BusinessRuleException"/> for every other reason.
    /// </summary>
    private async Task<Survey> LoadEligibleSurveyAsync(IAppDbContext db, Guid surveyId, string? accessKey, CancellationToken ct)
    {
        var survey = await db.LoadSurveyGraphAsync(s => s.Id == surveyId, ct)
            ?? throw new NotFoundException(SurveyEntity, surveyId);

        var verdict = await CheckEligibilityAsync(db, survey, ct);
        if (verdict.IsEligible)
        {
            if (survey.AccessPasswordHash is { } passwordHash && !accessKeys.IsValid(accessKey, survey.Id, passwordHash))
            {
                throw new ForbiddenException(PasswordRequiredMessage);
            }

            return survey;
        }

        var message = verdict.Message ?? SurveyEligibilityChecker.ClosedMessage;
        if (verdict.Eligibility == SurveyEligibility.LoginRequired)
        {
            throw new ForbiddenException(message);
        }

        throw new BusinessRuleException(message);
    }

    /// <summary>
    /// Re-checks the quota right before saving. <see cref="IAppDbContext"/> exposes no transactions, so
    /// two submissions racing within milliseconds could still exceed the quota by one; that is an
    /// accepted trade-off because the quota is a soft business limit, not a security boundary.
    /// </summary>
    private static async Task EnsureQuotaAvailableAsync(IAppDbContext db, Survey survey, CancellationToken ct)
    {
        if (survey.MaxResponses is { } max && await db.CountCompletedAsync(survey.Id, ct) >= max)
        {
            throw new BusinessRuleException(SurveyEligibilityChecker.QuotaReachedMessage);
        }
    }

    /// <summary>Copies the user's most recent draft (if any) into the session.</summary>
    private static async Task AttachLatestDraftAsync(
        IAppDbContext db, SurveySessionDto session, SurveyDefinitionDto definition, Guid userId, CancellationToken ct)
    {
        var draft = await db.LoadResponseWithAnswersAsync(q => q.DraftsOf(definition.Id, userId), tracked: false, ct);
        if (draft is null)
        {
            return;
        }

        var displayOrder = definition.AllQuestions()
            .Select((question, index) => (question.Id, index))
            .ToDictionary(x => x.Id, x => x.index);

        session.DraftResponseId = draft.Id;
        session.CurrentSectionIndex = ClampSectionIndex(draft.CurrentSectionIndex, definition.Sections.Count);
        session.Answers = draft.Answers
            .OrderBy(a => displayOrder.GetValueOrDefault(a.QuestionId, int.MaxValue))
            .Select(AnswerMapper.ToInputDto)
            .ToList();
    }

    /// <summary>
    /// Finds the draft to update (tracked, with answers): the one named by <paramref name="responseId"/>
    /// or else the user's latest draft of the survey. Returns null when the user has no draft.
    /// </summary>
    private static async Task<SurveyResponse?> FindDraftForUpdateAsync(
        IAppDbContext db, Guid surveyId, Guid userId, Guid? responseId, CancellationToken ct)
    {
        if (responseId is not { } id)
        {
            return await db.LoadResponseWithAnswersAsync(q => q.DraftsOf(surveyId, userId), tracked: true, ct);
        }

        var draft = await db.LoadResponseWithAnswersAsync(q => q.Where(r => r.Id == id), tracked: true, ct);

        // A response of another survey is reported as missing so ids cannot be probed across surveys.
        if (draft is null || draft.SurveyId != surveyId)
        {
            throw new NotFoundException(ResponseEntity, id);
        }

        if (draft.RespondentId != userId)
        {
            throw new ForbiddenException("You can only continue your own responses.");
        }

        if (draft.Status != ResponseStatus.InProgress)
        {
            throw new ForbiddenException("This response has already been submitted and can no longer be changed.");
        }

        return draft;
    }

    /// <summary>Creates and tracks a new in-progress response.</summary>
    private SurveyResponse StartResponse(IAppDbContext db, Guid surveyId, Guid? respondentId)
    {
        var response = new SurveyResponse
        {
            SurveyId = surveyId,
            RespondentId = respondentId,
            Status = ResponseStatus.InProgress,
            StartedAt = UtcNow,
        };
        db.Responses.Add(response);
        return response;
    }

    // ---------------------------------------------------------------- answers

    /// <summary>
    /// Sanitises the submitted answers, evaluates conditional logic, discards answers to hidden
    /// questions and validates the rest. Throws <see cref="AppValidationException"/> keyed by question id.
    /// </summary>
    private static Dictionary<Guid, AnswerInputDto> SanitizeAndValidateSubmission(
        SurveyDefinitionDto definition, IReadOnlyDictionary<Guid, QuestionDto> questions, IEnumerable<AnswerInputDto> rawAnswers)
    {
        var answers = ResponseAnswerWriter.Sanitize(questions, rawAnswers);
        var visibility = LogicEvaluator.Evaluate(definition, answers);

        // Answers to questions hidden by logic are never stored (they may be stale leftovers).
        var visibleAnswers = answers
            .Where(a => visibility.IsQuestionVisible(a.Key))
            .ToDictionary(a => a.Key, a => a.Value);

        var errors = ResponseValidator.Validate(definition, visibleAnswers, visibility);
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors.ToDictionary(e => e.Key.ToString(), e => e.Value.ToArray()));
        }

        return visibleAnswers;
    }

    /// <summary>Every question of the survey in display order, including unanswered ones.</summary>
    private static List<AnswerDetailDto> BuildAnswerDetails(SurveyDefinitionDto definition, IEnumerable<Answer> answers)
    {
        var answersByQuestion = answers
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => AnswerMapper.ToInputDto(g.First()));

        return definition.Sections
            .OrderBy(s => s.Order)
            .SelectMany(section => section.Questions
                .OrderBy(q => q.Order)
                .Select(question => ToAnswerDetail(section, question, answersByQuestion.GetValueOrDefault(question.Id))))
            .ToList();
    }

    private static AnswerDetailDto ToAnswerDetail(SectionDto section, QuestionDto question, AnswerInputDto? answer) => new()
    {
        QuestionId = question.Id,
        QuestionCode = question.Code ?? string.Empty,
        QuestionText = question.Text,
        QuestionType = question.Type,
        SectionTitle = section.Title,
        DisplayValue = AnswerFormatter.Format(question, answer),
        IsAnswered = answer is not null && answer.HasValue(question.Type),
    };

    private static Dictionary<Guid, QuestionDto> IndexQuestions(SurveyDefinitionDto definition) =>
        definition.AllQuestions().ToDictionary(q => q.Id);

    // ---------------------------------------------------------------- filters & helpers

    /// <summary>Applies the admin list filters (dates are inclusive UTC days on SubmittedAt ?? StartedAt).</summary>
    private static IQueryable<SurveyResponse> ApplyFilters(IQueryable<SurveyResponse> responses, ResponseQuery query)
    {
        if (query.Status is { } status)
        {
            responses = responses.Where(r => r.Status == status);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            responses = responses.Where(r => (r.SubmittedAt ?? r.StartedAt) >= fromUtc);
        }

        if (query.To is { } to && to < DateOnly.MaxValue)
        {
            // Inclusive end date: everything before midnight at the start of the following day.
            var beforeUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            responses = responses.Where(r => (r.SubmittedAt ?? r.StartedAt) < beforeUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ToLower() on the column translates to lower() on PostgreSQL and SQLite (no ILIKE).
            var term = query.Search.Trim().ToLowerInvariant();
            responses = responses.Where(r => r.Respondent != null
                && ((r.Respondent.Email != null && r.Respondent.Email.ToLower().Contains(term))
                    || (r.Respondent.DisplayName != null && r.Respondent.DisplayName.ToLower().Contains(term))));
        }

        return responses;
    }

    private void EnsureAdmin(string action)
    {
        if (!currentUser.IsAdmin)
        {
            throw new ForbiddenException($"Only administrators can {action}.");
        }
    }

    /// <summary>Runs a FluentValidation validator and converts failures into <see cref="AppValidationException"/>.</summary>
    private static async Task ValidateAsync<T>(IValidator<T> validator, T instance, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(instance, ct);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        throw new AppValidationException(errors);
    }

    private static SurveySessionDto ToSession(EligibilityVerdict verdict) => new()
    {
        Eligibility = verdict.Eligibility,
        Message = verdict.Message,
    };

    private static AvailableSurveyDto ToAvailableDto(AvailableSurveyRow row, EligibilityVerdict verdict) => new()
    {
        SurveyId = row.Survey.Id,
        Title = row.Survey.Title,
        Description = row.Survey.Description,
        Slug = row.Survey.Slug,
        ClosesAt = row.Survey.ClosesAt,
        QuestionCount = row.QuestionCount,
        EstimatedMinutes = EstimateMinutes(row.QuestionCount),
        AllowAnonymous = row.Survey.AllowAnonymous,
        HasCompleted = row.UserCompleted,
        HasDraft = row.UserHasDraft,
        CanRespond = verdict.IsEligible,
    };

    /// <summary>About <see cref="SecondsPerQuestion"/> seconds per question, rounded up, at least one minute.</summary>
    private static int EstimateMinutes(int questionCount) =>
        Math.Max(1, (int)Math.Ceiling(questionCount * SecondsPerQuestion / 60.0));

    private static int ClampSectionIndex(int index, int sectionCount) =>
        sectionCount <= 0 ? 0 : Math.Clamp(index, 0, sectionCount - 1);

    private static string? TruncateUserAgent(string? userAgent)
    {
        var trimmed = userAgent?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaxUserAgentLength ? trimmed : trimmed[..MaxUserAgentLength];
    }

    private static string RespondentName(string? displayName, string? email) =>
        !string.IsNullOrWhiteSpace(displayName) ? displayName
        : !string.IsNullOrWhiteSpace(email) ? email
        : AnonymousName;

    private static double? DurationSeconds(ResponseStatus status, DateTime startedAt, DateTime? submittedAt) =>
        status == ResponseStatus.Completed && submittedAt is { } submitted
            ? Math.Max(0, (submitted - startedAt).TotalSeconds)
            : null;

    /// <summary>Projection row of <see cref="ListAvailableAsync"/>.</summary>
    private sealed record AvailableSurveyRow(
        Survey Survey, int QuestionCount, int CompletedCount, bool UserCompleted, bool UserHasDraft);
}
