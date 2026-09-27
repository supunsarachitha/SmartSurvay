using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Application.Surveys;

/// <summary>
/// Entity → DTO mapping for the survey aggregate (manual mapping keeps it explicit and fast).
/// DTO → entity reconciliation lives in <c>SurveyService</c> because it needs change tracking.
/// </summary>
public static class SurveyMapper
{
    /// <summary>
    /// Maps a fully loaded survey (sections, questions, options, logic rules and conditions) to a
    /// definition DTO with children sorted by their Order values. Settings objects are cloned so
    /// the DTO never shares mutable state with a tracked entity.
    /// </summary>
    public static SurveyDefinitionDto ToDefinitionDto(this Survey survey)
    {
        ArgumentNullException.ThrowIfNull(survey);

        // Questions may be attached via Survey.Questions and/or Section.Questions depending on the
        // query; group by SectionId from the survey-level collection when it is populated.
        var questionsBySection = (survey.Questions.Count > 0
                ? survey.Questions
                : survey.Sections.SelectMany(s => s.Questions))
            .GroupBy(q => q.SectionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(q => q.Order).ToList());

        return new SurveyDefinitionDto
        {
            Id = survey.Id,
            Title = survey.Title,
            Description = survey.Description,
            Slug = survey.Slug,
            Status = survey.Status,
            IsTemplate = survey.IsTemplate,
            AllowAnonymous = survey.AllowAnonymous,
            AllowMultipleResponses = survey.AllowMultipleResponses,
            ShowProgressBar = survey.ShowProgressBar,
            ShowQuestionNumbers = survey.ShowQuestionNumbers,
            OpensAt = survey.OpensAt,
            ClosesAt = survey.ClosesAt,
            MaxResponses = survey.MaxResponses,
            WelcomeMessage = survey.WelcomeMessage,
            ThankYouMessage = survey.ThankYouMessage,
            Version = survey.Version,
            CreatedAt = survey.CreatedAt,
            UpdatedAt = survey.UpdatedAt,
            PublishedAt = survey.PublishedAt,
            Sections = survey.Sections
                .OrderBy(s => s.Order)
                .Select(s => new SectionDto
                {
                    Id = s.Id,
                    Title = s.Title,
                    Description = s.Description,
                    Order = s.Order,
                    Questions = questionsBySection.TryGetValue(s.Id, out var qs)
                        ? qs.Select(ToDto).ToList()
                        : [],
                })
                .ToList(),
            LogicRules = survey.LogicRules.Select(ToDto).ToList(),
        };
    }

    /// <summary>Maps a question with its options.</summary>
    public static QuestionDto ToDto(this Question q) => new()
    {
        Id = q.Id,
        Type = q.Type,
        Text = q.Text,
        Description = q.Description,
        Code = q.Code,
        Order = q.Order,
        IsRequired = q.IsRequired,
        Settings = q.Settings.Clone(),
        Options = q.Options.OrderBy(o => o.Order).Select(ToDto).ToList(),
    };

    /// <summary>Maps an option.</summary>
    public static OptionDto ToDto(this QuestionOption o) => new()
    {
        Id = o.Id,
        Text = o.Text,
        Value = o.Value,
        Order = o.Order,
        AllowsFreeText = o.AllowsFreeText,
        FreeTextPlaceholder = o.FreeTextPlaceholder,
    };

    /// <summary>Maps a logic rule with its conditions.</summary>
    public static LogicRuleDto ToDto(this LogicRule r) => new()
    {
        Id = r.Id,
        TargetQuestionId = r.TargetQuestionId,
        TargetSectionId = r.TargetSectionId,
        Action = r.Action,
        MatchType = r.MatchType,
        Conditions = r.Conditions.Select(c => new LogicConditionDto
        {
            Id = c.Id,
            SourceQuestionId = c.SourceQuestionId,
            Operator = c.Operator,
            OptionId = c.OptionId,
            Value = c.Value,
        }).ToList(),
    };
}
