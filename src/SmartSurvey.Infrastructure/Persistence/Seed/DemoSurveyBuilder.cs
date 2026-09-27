using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Infrastructure.Persistence.Seed;

/// <summary>
/// Small fluent helper that assembles a complete survey entity graph (pages, questions, options and
/// logic rules) for the demo data. Every foreign key is set explicitly, so the graph can be mapped
/// with <c>SurveyMapper</c> (e.g. for the logic evaluator) before it is saved.
/// </summary>
internal sealed class DemoSurveyBuilder
{
    private SurveySection? _section;

    /// <summary>Starts a new survey graph.</summary>
    /// <param name="survey">Survey entity with its settings already filled in.</param>
    public DemoSurveyBuilder(Survey survey)
    {
        Survey = survey;
    }

    /// <summary>The survey being built.</summary>
    public Survey Survey { get; }

    /// <summary>Appends a page; subsequent questions are placed on it.</summary>
    public SurveySection Section(string title, string? description = null)
    {
        _section = new SurveySection
        {
            SurveyId = Survey.Id,
            Title = title,
            Description = description,
            Order = Survey.Sections.Count,
        };
        Survey.Sections.Add(_section);
        return _section;
    }

    /// <summary>Appends a non-choice question (text, number, e-mail, date, rating, scale) to the current page.</summary>
    public Question Input(
        string code, QuestionType type, string text, bool required = false, string? help = null, Action<QuestionSettings>? settings = null)
    {
        if (type.IsChoice())
        {
            throw new ArgumentException("Use Choice() for questions with answer options.", nameof(type));
        }

        return AddQuestion(code, type, text, required, help, settings);
    }

    /// <summary>
    /// Appends a choice question (radio, checkbox, dropdown) to the current page. When
    /// <paramref name="otherOption"/> is given, a combined "choice + free text" option with that label
    /// is appended as the last option.
    /// </summary>
    public Question Choice(
        string code,
        QuestionType type,
        string text,
        IReadOnlyList<string> options,
        bool required = false,
        string? help = null,
        string? otherOption = null,
        Action<QuestionSettings>? settings = null)
    {
        if (!type.IsChoice())
        {
            throw new ArgumentException("Use Input() for questions without answer options.", nameof(type));
        }

        var question = AddQuestion(code, type, text, required, help, settings);
        foreach (var label in options)
        {
            AddOption(question, label, allowsFreeText: false);
        }

        if (otherOption is not null)
        {
            AddOption(question, otherOption, allowsFreeText: true);
        }

        return question;
    }

    /// <summary>Adds a rule that shows or hides a question.</summary>
    public LogicRule QuestionRule(Question target, LogicAction action, LogicMatchType match, params LogicCondition[] conditions) =>
        AddRule(action, match, conditions, targetQuestionId: target.Id, targetSectionId: null);

    /// <summary>Adds a rule that shows or hides a whole page.</summary>
    public LogicRule SectionRule(SurveySection target, LogicAction action, LogicMatchType match, params LogicCondition[] conditions) =>
        AddRule(action, match, conditions, targetQuestionId: null, targetSectionId: target.Id);

    /// <summary>Condition "<paramref name="source"/> has the option labelled <paramref name="optionText"/> selected".</summary>
    public static LogicCondition Selected(Question source, string optionText) => new()
    {
        SourceQuestionId = source.Id,
        Operator = source.Type.IsMultiSelect() ? ConditionOperator.Contains : ConditionOperator.Equals,
        OptionId = source.Option(optionText).Id,
    };

    /// <summary>Condition comparing a numeric/date/text answer with a value.</summary>
    public static LogicCondition Compare(Question source, ConditionOperator op, string value) => new()
    {
        SourceQuestionId = source.Id,
        Operator = op,
        Value = value,
    };

    private Question AddQuestion(
        string code, QuestionType type, string text, bool required, string? help, Action<QuestionSettings>? settings)
    {
        var section = _section ?? throw new InvalidOperationException("Add a section before adding questions.");
        var question = new Question
        {
            SurveyId = Survey.Id,
            SectionId = section.Id,
            Type = type,
            Text = text,
            Description = help,
            Code = code,
            Order = section.Questions.Count,
            IsRequired = required,
        };
        settings?.Invoke(question.Settings);

        // The question is reachable through both navigations; EF Core treats it as one entity.
        section.Questions.Add(question);
        Survey.Questions.Add(question);
        return question;
    }

    private static void AddOption(Question question, string label, bool allowsFreeText) =>
        question.Options.Add(new QuestionOption
        {
            QuestionId = question.Id,
            Text = label,
            Order = question.Options.Count,
            AllowsFreeText = allowsFreeText,
            FreeTextPlaceholder = allowsFreeText ? "Please specify" : null,
        });

    private LogicRule AddRule(
        LogicAction action, LogicMatchType match, LogicCondition[] conditions, Guid? targetQuestionId, Guid? targetSectionId)
    {
        var rule = new LogicRule
        {
            SurveyId = Survey.Id,
            TargetQuestionId = targetQuestionId,
            TargetSectionId = targetSectionId,
            Action = action,
            MatchType = match,
        };

        foreach (var condition in conditions)
        {
            condition.LogicRuleId = rule.Id;
            rule.Conditions.Add(condition);
        }

        Survey.LogicRules.Add(rule);
        return rule;
    }
}

/// <summary>Lookup helpers for demo survey graphs.</summary>
internal static class DemoSurveyExtensions
{
    /// <summary>The option of <paramref name="question"/> with the given label (throws when missing).</summary>
    public static QuestionOption Option(this Question question, string text) =>
        question.Options.FirstOrDefault(o => o.Text == text)
        ?? throw new InvalidOperationException($"Question {question.Code} has no option '{text}'.");

    /// <summary>The question of <paramref name="survey"/> with the given code (throws when missing).</summary>
    public static Question QuestionByCode(this Survey survey, string code) =>
        survey.Questions.FirstOrDefault(q => q.Code == code)
        ?? throw new InvalidOperationException($"Survey '{survey.Title}' has no question {code}.");
}
