using SmartSurvey.Application.Logic;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Infrastructure.Persistence.Seed;

/// <summary>Accounts the demo responses are attributed to.</summary>
/// <param name="DemoUserId">Demo respondent (null when it could not be created).</param>
/// <param name="DemoUserEmail">Demo respondent's e-mail (answers the optional e-mail question).</param>
/// <param name="EmployeeIds">Demo employee accounts (respondents of the login-required pulse survey).</param>
internal sealed record DemoRespondentAccounts(Guid? DemoUserId, string? DemoUserEmail, IReadOnlyList<Guid> EmployeeIds);

/// <summary>Generated demo responses.</summary>
/// <param name="Responses">Responses with answers, ready to be added to the context.</param>
/// <param name="Discarded">Generated completed responses that failed validation and were dropped (expected: 0).</param>
internal sealed record DemoResponseSet(IReadOnlyList<SurveyResponse> Responses, int Discarded);

/// <summary>
/// Generates realistic, deterministic demo responses for the demo surveys.
/// </summary>
/// <remarks>
/// Every response is produced the way a real respondent would fill in the runner: pages and questions
/// are walked in display order and only questions that are visible given the answers so far (per the
/// <see cref="LogicEvaluator"/>) are answered. Completed responses are then checked with
/// <see cref="ResponseValidator"/> exactly like a server-side submission, so the demo data is always
/// valid. Timestamps are spread over the last <see cref="HistoryDays"/> days before the seeding time,
/// weighted towards recent days.
/// </remarks>
internal sealed class DemoResponseGenerator(Random random, DateTime utcNow)
{
    /// <summary>Days of response history (including today).</summary>
    public const int HistoryDays = 60;

    /// <summary>Completed customer-survey responses.</summary>
    public const int CustomerCompleted = 150;

    /// <summary>Completed customer-survey responses submitted by the demo user (the rest are anonymous).</summary>
    public const int CustomerCompletedByDemoUser = 4;

    /// <summary>In-progress customer-survey drafts (one by the demo user, the rest by demo employees).</summary>
    public const int CustomerInProgress = 8;

    /// <summary>Completed engagement-survey responses (one per demo employee).</summary>
    public const int EngagementCompleted = 42;

    /// <summary>In-progress engagement-survey drafts (by other demo employees).</summary>
    public const int EngagementInProgress = 3;

    /// <summary>Days of history of the engagement survey (it was published later).</summary>
    private const int EngagementHistoryDays = 40;

    /// <summary>Drafts were started within this many days.</summary>
    private const int DraftHistoryDays = 10;

    /// <summary>Generates all demo responses.</summary>
    public DemoResponseSet Generate(DemoSurveys surveys, DemoRespondentAccounts accounts)
    {
        var responses = new List<SurveyResponse>();
        var discarded = 0;

        void AddCompleted(SurveyResponse? response)
        {
            if (response is null)
            {
                discarded++;
            }
            else
            {
                responses.Add(response);
            }
        }

        // Customer survey: mostly anonymous; a few completed responses and the latest draft by the demo user.
        var customer = surveys.Customer.ToDefinitionDto();
        var demoUserSlots = DemoUserSlots(accounts.DemoUserId);
        for (var i = 0; i < CustomerCompleted; i++)
        {
            var byDemoUser = demoUserSlots.Contains(i);
            var respondent = new CustomerRespondent(random, utcNow, byDemoUser ? accounts.DemoUserEmail : null);
            AddCompleted(Completed(customer, respondent, byDemoUser ? accounts.DemoUserId : null, HistoryDays));
        }

        var draftOwners = new List<Guid?> { accounts.DemoUserId };
        draftOwners.AddRange(accounts.EmployeeIds.Take(CustomerInProgress - 1).Select(id => (Guid?)id));
        foreach (var owner in draftOwners.Where(o => o.HasValue))
        {
            var email = owner == accounts.DemoUserId ? accounts.DemoUserEmail : null;
            responses.Add(InProgress(customer, new CustomerRespondent(random, utcNow, email), owner));
        }

        // Engagement pulse: login required and one response per user, so every response has its own employee.
        var engagement = surveys.Engagement.ToDefinitionDto();
        var employees = accounts.EmployeeIds;
        foreach (var employeeId in employees.Take(EngagementCompleted))
        {
            AddCompleted(Completed(engagement, new EmployeeRespondent(random), employeeId, EngagementHistoryDays));
        }

        foreach (var employeeId in employees.Skip(EngagementCompleted).Take(EngagementInProgress))
        {
            responses.Add(InProgress(engagement, new EmployeeRespondent(random), employeeId));
        }

        return new DemoResponseSet(responses, discarded);
    }

    /// <summary>Indexes of the completed customer responses attributed to the demo user (spread over the history).</summary>
    private static HashSet<int> DemoUserSlots(Guid? demoUserId) => demoUserId is null
        ? []
        : Enumerable.Range(0, CustomerCompletedByDemoUser).Select(i => 5 + (i * (CustomerCompleted / CustomerCompletedByDemoUser))).ToHashSet();

    /// <summary>A completed response, or null when (unexpectedly) the generated answers are invalid.</summary>
    private SurveyResponse? Completed(SurveyDefinitionDto survey, IDemoRespondent respondent, Guid? respondentId, int historyDays)
    {
        var (answers, pages) = AnswerVisibleQuestions(survey, respondent, pageLimit: int.MaxValue);
        var visibility = LogicEvaluator.Evaluate(survey, answers);
        if (ResponseValidator.Validate(survey, answers, visibility).Count > 0)
        {
            return null;
        }

        var submittedAt = RandomMoment(historyDays);
        var startedAt = submittedAt.AddSeconds(-random.Next(60, 12 * 60 + 1)); // 1–12 minutes
        return NewResponse(survey, respondentId, ResponseStatus.Completed, startedAt, submittedAt, pages - 1, answers);
    }

    /// <summary>A draft with the first page(s) answered, as saved by the runner when a respondent leaves early.</summary>
    private SurveyResponse InProgress(SurveyDefinitionDto survey, IDemoRespondent respondent, Guid? respondentId)
    {
        var pageLimit = random.Next(1, Math.Max(2, survey.Sections.Count));
        var (answers, pages) = AnswerVisibleQuestions(survey, respondent, pageLimit);

        var startedAt = RandomMoment(DraftHistoryDays).AddMinutes(-10);
        var savedAt = startedAt.AddMinutes(random.Next(1, 7));
        var response = NewResponse(survey, respondentId, ResponseStatus.InProgress, startedAt, submittedAt: null,
            currentSectionIndex: Math.Min(pages, survey.Sections.Count - 1), answers);
        response.UpdatedAt = savedAt;
        return response;
    }

    /// <summary>
    /// Walks the pages in display order and lets the respondent answer each visible question. Conditions
    /// only reference earlier questions, so the visibility evaluated when a question is reached is final.
    /// </summary>
    /// <returns>The sanitised answers and the number of visible pages that were answered.</returns>
    private static (Dictionary<Guid, AnswerInputDto> Answers, int Pages) AnswerVisibleQuestions(
        SurveyDefinitionDto survey, IDemoRespondent respondent, int pageLimit)
    {
        var answers = new Dictionary<Guid, AnswerInputDto>();
        var pages = 0;

        foreach (var section in survey.Sections.OrderBy(s => s.Order))
        {
            if (pages == pageLimit)
            {
                break;
            }

            if (!LogicEvaluator.Evaluate(survey, answers).IsSectionVisible(section.Id))
            {
                continue;
            }

            foreach (var question in section.Questions.OrderBy(q => q.Order))
            {
                if (LogicEvaluator.Evaluate(survey, answers).IsQuestionVisible(question.Id)
                    && respondent.AnswerFor(question) is { } raw
                    && ResponseValidator.Sanitize(question, raw) is { } clean)
                {
                    answers[question.Id] = clean;
                }
            }

            pages++;
        }

        return (answers, pages);
    }

    /// <summary>
    /// A moment within the last <paramref name="days"/> days (today included), between 07:00 and 22:00 UTC,
    /// weighted towards recent days; never in the future.
    /// </summary>
    private DateTime RandomMoment(int days)
    {
        var daysAgo = (int)Math.Floor(days * Math.Pow(random.NextDouble(), 1.35));
        var moment = utcNow.Date.AddDays(-daysAgo).AddMinutes(random.Next(7 * 60, 22 * 60));
        return moment <= utcNow ? moment : utcNow.AddMinutes(-random.Next(2, 90));
    }

    private SurveyResponse NewResponse(
        SurveyDefinitionDto survey,
        Guid? respondentId,
        ResponseStatus status,
        DateTime startedAt,
        DateTime? submittedAt,
        int currentSectionIndex,
        IReadOnlyDictionary<Guid, AnswerInputDto> answers)
    {
        var response = new SurveyResponse
        {
            SurveyId = survey.Id,
            RespondentId = respondentId,
            Status = status,
            StartedAt = startedAt,
            UpdatedAt = submittedAt,
            SubmittedAt = submittedAt,
            CurrentSectionIndex = Math.Max(0, currentSectionIndex),
            UserAgent = random.Pick(DemoPhrases.UserAgents),
        };

        // Answers are added in the display order of their questions.
        foreach (var question in survey.AllQuestions())
        {
            if (answers.TryGetValue(question.Id, out var input))
            {
                var answer = AnswerMapper.ToEntity(input, question.Type);
                answer.ResponseId = response.Id;
                response.Answers.Add(answer);
            }
        }

        return response;
    }
}
