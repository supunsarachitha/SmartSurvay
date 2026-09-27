using System.Globalization;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Surveys;

/// <summary>
/// Brings a survey design submitted by the builder, the API or an import file into canonical form
/// before it is validated and saved. The design is modified in place, so callers pass a private
/// copy (see <see cref="SurveyDefinitionCloner.Copy"/>).
/// </summary>
/// <remarks>
/// Steps, in order:
/// <list type="number">
/// <item>Trim all texts (optional texts that end up empty become null); lowercase the slug; treat
/// schedule dates as UTC.</item>
/// <item>Replace null collections/settings sent by JSON clients and drop null list entries.</item>
/// <item>Give every item with <see cref="Guid.Empty"/> a new id (before references are validated).</item>
/// <item>Add a default "Page 1" when the design has no pages.</item>
/// <item>Renumber <c>Order</c> to 0..n-1 for pages, questions per page and options per question,
/// keeping the submitted relative order; remove options from non-choice questions.</item>
/// <item>Generate codes Q1, Q2, … in display order for questions without a code, skipping codes
/// that are already in use (case-insensitive).</item>
/// <item>Clean logic conditions: unary operators carry no operand, choice sources compare an option
/// (a GUID sent as the value is accepted as the option id), other sources compare a value.</item>
/// </list>
/// </remarks>
internal static class SurveyNormalizer
{
    /// <summary>Title of the page added to designs that have none.</summary>
    public const string DefaultSectionTitle = "Page 1";

    /// <summary>Normalises <paramref name="survey"/> in place.</summary>
    public static void Normalize(SurveyDefinitionDto survey)
    {
        ArgumentNullException.ThrowIfNull(survey);

        NormalizeSurveyFields(survey);
        RemoveNulls(survey);
        AssignMissingIds(survey);

        if (survey.Sections.Count == 0)
        {
            survey.Sections.Add(new SectionDto { Title = DefaultSectionTitle });
        }

        NormalizeStructure(survey);
        GenerateMissingCodes(survey);
        NormalizeConditions(survey);
    }

    private static void NormalizeSurveyFields(SurveyDefinitionDto survey)
    {
        survey.Title = TrimOrEmpty(survey.Title);
        survey.Description = TrimToNull(survey.Description);
        survey.Slug = TrimToNull(survey.Slug)?.ToLowerInvariant();
        survey.WelcomeMessage = TrimToNull(survey.WelcomeMessage);
        survey.ThankYouMessage = TrimToNull(survey.ThankYouMessage);
        survey.OpensAt = AsUtc(survey.OpensAt);
        survey.ClosesAt = AsUtc(survey.ClosesAt);
    }

    /// <summary>JSON clients may send explicit nulls; the rest of the pipeline relies on non-null collections.</summary>
    private static void RemoveNulls(SurveyDefinitionDto survey)
    {
        survey.Sections ??= [];
        survey.Sections.RemoveAll(s => s is null);
        foreach (var section in survey.Sections)
        {
            section.Questions ??= [];
            section.Questions.RemoveAll(q => q is null);
            foreach (var question in section.Questions)
            {
                question.Settings ??= new QuestionSettings();
                question.Options ??= [];
                question.Options.RemoveAll(o => o is null);
            }
        }

        survey.LogicRules ??= [];
        survey.LogicRules.RemoveAll(r => r is null);
        foreach (var rule in survey.LogicRules)
        {
            rule.Conditions ??= [];
            rule.Conditions.RemoveAll(c => c is null);
        }
    }

    private static void AssignMissingIds(SurveyDefinitionDto survey)
    {
        survey.Id = EnsureId(survey.Id);
        foreach (var section in survey.Sections)
        {
            section.Id = EnsureId(section.Id);
            foreach (var question in section.Questions)
            {
                question.Id = EnsureId(question.Id);
                foreach (var option in question.Options)
                {
                    option.Id = EnsureId(option.Id);
                }
            }
        }

        foreach (var rule in survey.LogicRules)
        {
            rule.Id = EnsureId(rule.Id);
            foreach (var condition in rule.Conditions)
            {
                condition.Id = EnsureId(condition.Id);
            }
        }
    }

    /// <summary>Sorts and renumbers pages, questions and options and trims their texts.</summary>
    private static void NormalizeStructure(SurveyDefinitionDto survey)
    {
        // OrderBy is stable: items with equal Order keep their submitted relative position.
        survey.Sections = survey.Sections.OrderBy(s => s.Order).ToList();
        for (var s = 0; s < survey.Sections.Count; s++)
        {
            var section = survey.Sections[s];
            section.Order = s;
            section.Title = TrimOrEmpty(section.Title);
            section.Description = TrimToNull(section.Description);

            section.Questions = section.Questions.OrderBy(q => q.Order).ToList();
            for (var q = 0; q < section.Questions.Count; q++)
            {
                section.Questions[q].Order = q;
                NormalizeQuestion(section.Questions[q]);
            }
        }
    }

    private static void NormalizeQuestion(QuestionDto question)
    {
        question.Text = TrimOrEmpty(question.Text);
        question.Description = TrimToNull(question.Description);
        question.Code = TrimToNull(question.Code);
        question.Settings.Placeholder = TrimToNull(question.Settings.Placeholder);
        question.Settings.ScaleMinLabel = TrimToNull(question.Settings.ScaleMinLabel);
        question.Settings.ScaleMaxLabel = TrimToNull(question.Settings.ScaleMaxLabel);

        if (!question.Type.IsChoice())
        {
            // Options are meaningless for other types (e.g. left over after a type change).
            question.Options.Clear();
            return;
        }

        question.Options = question.Options.OrderBy(o => o.Order).ToList();
        for (var o = 0; o < question.Options.Count; o++)
        {
            var option = question.Options[o];
            option.Order = o;
            option.Text = TrimOrEmpty(option.Text);
            option.Value = TrimToNull(option.Value);
            option.FreeTextPlaceholder = option.AllowsFreeText ? TrimToNull(option.FreeTextPlaceholder) : null;
        }
    }

    /// <summary>Assigns Q1, Q2, … (display order) to questions without a code, never reusing a taken code.</summary>
    private static void GenerateMissingCodes(SurveyDefinitionDto survey)
    {
        var questions = survey.AllQuestions().ToList();
        var used = new HashSet<string>(
            questions.Where(q => q.Code is not null).Select(q => q.Code!),
            StringComparer.OrdinalIgnoreCase);

        var next = 1;
        foreach (var question in questions.Where(q => q.Code is null))
        {
            string code;
            do
            {
                code = "Q" + next.ToString(CultureInfo.InvariantCulture);
                next++;
            }
            while (!used.Add(code));

            question.Code = code;
        }
    }

    private static void NormalizeConditions(SurveyDefinitionDto survey)
    {
        var questions = survey.Sections
            .SelectMany(s => s.Questions)
            .GroupBy(q => q.Id)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var condition in survey.LogicRules.SelectMany(r => r.Conditions))
        {
            condition.Value = TrimToNull(condition.Value);

            if (condition.Operator.IsUnary())
            {
                condition.OptionId = null;
                condition.Value = null;
            }
            else if (questions.TryGetValue(condition.SourceQuestionId, out var source))
            {
                if (source.Type.IsChoice())
                {
                    // ConditionMatcher accepts an option id sent as the value; store it canonically.
                    if (condition.OptionId is null && Guid.TryParse(condition.Value, out var optionId))
                    {
                        condition.OptionId = optionId;
                    }

                    condition.Value = null;
                }
                else
                {
                    condition.OptionId = null;
                }
            }
        }
    }

    private static Guid EnsureId(Guid id) => id == Guid.Empty ? Guid.NewGuid() : id;

    private static string TrimOrEmpty(string? value) => value?.Trim() ?? string.Empty;

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Schedule dates are UTC; local times are converted, unspecified ones are taken as UTC.</summary>
    private static DateTime? AsUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        { } other => DateTime.SpecifyKind(other, DateTimeKind.Utc),
    };
}
