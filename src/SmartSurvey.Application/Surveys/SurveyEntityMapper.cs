using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Surveys;

/// <summary>
/// DTO → entity mapping for the survey design, used when creating a survey and when reconciling an
/// update. Expects a normalised design (non-null collections, ids and codes assigned). Lifecycle
/// fields (status, publication/closing timestamps, audit stamps, version) are never taken from the DTO.
/// </summary>
internal static class SurveyEntityMapper
{
    /// <summary>Builds a new Draft survey (version 1) with its complete design graph.</summary>
    /// <param name="design">Normalised design; its ids become the entity ids.</param>
    /// <param name="slug">Resolved, unique slug.</param>
    public static Survey ToNewSurvey(SurveyDefinitionDto design, string slug)
    {
        var survey = new Survey { Id = design.Id, Slug = slug, Status = SurveyStatus.Draft, Version = 1 };
        ApplySurveyFields(design, survey);

        foreach (var sectionDto in design.Sections)
        {
            var section = new SurveySection { Id = sectionDto.Id, SurveyId = survey.Id };
            ApplySection(sectionDto, section);
            survey.Sections.Add(section);

            foreach (var questionDto in sectionDto.Questions)
            {
                var question = new Question { Id = questionDto.Id, SurveyId = survey.Id };
                ApplyQuestion(questionDto, question, section.Id);
                section.Questions.Add(question);
                survey.Questions.Add(question);

                foreach (var optionDto in questionDto.Options)
                {
                    var option = new QuestionOption { Id = optionDto.Id };
                    ApplyOption(optionDto, option, question.Id);
                    question.Options.Add(option);
                }
            }
        }

        foreach (var ruleDto in design.LogicRules)
        {
            var rule = new LogicRule { Id = ruleDto.Id, SurveyId = survey.Id };
            ApplyRule(ruleDto, rule);
            survey.LogicRules.Add(rule);

            foreach (var conditionDto in ruleDto.Conditions)
            {
                var condition = new LogicCondition { Id = conditionDto.Id };
                ApplyCondition(conditionDto, condition, rule.Id);
                rule.Conditions.Add(condition);
            }
        }

        return survey;
    }

    /// <summary>Copies the editable survey settings (everything except slug and lifecycle fields).</summary>
    public static void ApplySurveyFields(SurveyDefinitionDto design, Survey survey)
    {
        survey.Title = design.Title;
        survey.Description = design.Description;
        survey.IsTemplate = design.IsTemplate;
        survey.AllowAnonymous = design.AllowAnonymous;
        survey.AllowMultipleResponses = design.AllowMultipleResponses;
        survey.ShowProgressBar = design.ShowProgressBar;
        survey.ShowQuestionNumbers = design.ShowQuestionNumbers;
        survey.OpensAt = design.OpensAt;
        survey.ClosesAt = design.ClosesAt;
        survey.MaxResponses = design.MaxResponses;
        survey.WelcomeMessage = design.WelcomeMessage;
        survey.ThankYouMessage = design.ThankYouMessage;
    }

    /// <summary>Copies page fields.</summary>
    public static void ApplySection(SectionDto dto, SurveySection section)
    {
        section.Title = dto.Title;
        section.Description = dto.Description;
        section.Order = dto.Order;
    }

    /// <summary>Copies question fields and places the question on <paramref name="sectionId"/>.</summary>
    public static void ApplyQuestion(QuestionDto dto, Question question, Guid sectionId)
    {
        question.SectionId = sectionId;
        question.Type = dto.Type;
        question.Text = dto.Text;
        question.Description = dto.Description;
        question.Code = dto.Code ?? string.Empty;
        question.Order = dto.Order;
        question.IsRequired = dto.IsRequired;
        question.Settings = dto.Settings.Clone(); // never share the mutable settings object with the DTO
    }

    /// <summary>Copies option fields and attaches the option to <paramref name="questionId"/>.</summary>
    public static void ApplyOption(OptionDto dto, QuestionOption option, Guid questionId)
    {
        option.QuestionId = questionId;
        option.Text = dto.Text;
        option.Value = dto.Value;
        option.Order = dto.Order;
        option.AllowsFreeText = dto.AllowsFreeText;
        option.FreeTextPlaceholder = dto.FreeTextPlaceholder;
    }

    /// <summary>Copies rule fields (target, action, match type).</summary>
    public static void ApplyRule(LogicRuleDto dto, LogicRule rule)
    {
        rule.TargetQuestionId = dto.TargetQuestionId;
        rule.TargetSectionId = dto.TargetSectionId;
        rule.Action = dto.Action;
        rule.MatchType = dto.MatchType;
    }

    /// <summary>Copies condition fields and attaches the condition to <paramref name="ruleId"/>.</summary>
    public static void ApplyCondition(LogicConditionDto dto, LogicCondition condition, Guid ruleId)
    {
        condition.LogicRuleId = ruleId;
        condition.SourceQuestionId = dto.SourceQuestionId;
        condition.Operator = dto.Operator;
        condition.OptionId = dto.OptionId;
        condition.Value = dto.Value;
    }
}
