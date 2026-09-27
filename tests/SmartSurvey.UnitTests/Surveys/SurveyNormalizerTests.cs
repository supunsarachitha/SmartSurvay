using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary>Direct tests of <see cref="SurveyNormalizer"/> (pure, no database).</summary>
public class SurveyNormalizerTests
{
    [Fact]
    public void Sample_survey_is_already_canonical()
    {
        var s = SampleSurveys.CustomerFeedback();
        var before = s.Definition.StructureFingerprint();

        SurveyNormalizer.Normalize(s.Definition);

        Assert.Equal(before, s.Definition.StructureFingerprint());
    }

    [Fact]
    public void Null_collections_and_entries_from_json_clients_are_repaired()
    {
        var survey = new SurveyDefinitionDto
        {
            Title = "T",
            Sections =
            [
                null!,
                new SectionDto
                {
                    Title = "P",
                    Questions = [new QuestionDto { Text = "Q", Settings = null!, Options = null! }, null!],
                },
            ],
            LogicRules = [null!, new LogicRuleDto { Conditions = null! }],
        };

        SurveyNormalizer.Normalize(survey);

        var question = Assert.Single(Assert.Single(survey.Sections).Questions);
        Assert.NotNull(question.Settings);
        Assert.Empty(question.Options);
        Assert.Empty(Assert.Single(survey.LogicRules).Conditions);
    }

    [Fact]
    public void Null_top_level_lists_become_a_default_page_and_no_rules()
    {
        var survey = new SurveyDefinitionDto { Title = "T", Sections = null!, LogicRules = null! };

        SurveyNormalizer.Normalize(survey);

        Assert.Equal(SurveyNormalizer.DefaultSectionTitle, Assert.Single(survey.Sections).Title);
        Assert.Empty(survey.LogicRules);
    }

    [Fact]
    public void Optional_texts_that_are_blank_become_null_and_slug_is_lowercased()
    {
        var survey = new SurveyDefinitionDto
        {
            Title = " T ",
            Description = " \t ",
            WelcomeMessage = "",
            ThankYouMessage = "  Thanks  ",
            Slug = "  My-Slug ",
        };

        SurveyNormalizer.Normalize(survey);

        Assert.Equal("T", survey.Title);
        Assert.Null(survey.Description);
        Assert.Null(survey.WelcomeMessage);
        Assert.Equal("Thanks", survey.ThankYouMessage);
        Assert.Equal("my-slug", survey.Slug);
    }

    [Fact]
    public void Schedule_dates_are_treated_as_utc()
    {
        var unspecified = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Unspecified);
        var local = new DateTime(2026, 6, 30, 8, 0, 0, DateTimeKind.Local);
        var survey = new SurveyDefinitionDto { Title = "T", OpensAt = unspecified, ClosesAt = local };

        SurveyNormalizer.Normalize(survey);

        Assert.Equal(DateTimeKind.Utc, survey.OpensAt!.Value.Kind);
        Assert.Equal(unspecified.Ticks, survey.OpensAt.Value.Ticks);
        Assert.Equal(DateTimeKind.Utc, survey.ClosesAt!.Value.Kind);
        Assert.Equal(local.ToUniversalTime(), survey.ClosesAt.Value);
    }

    [Fact]
    public void Scale_labels_and_placeholders_are_trimmed()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Rating.Type = QuestionType.Scale;
        s.Rating.Settings.ScaleMinLabel = "  Not likely ";
        s.Rating.Settings.ScaleMaxLabel = "   ";
        s.Email.Settings.Placeholder = " you@example.com ";

        SurveyNormalizer.Normalize(s.Definition);

        Assert.Equal("Not likely", s.Rating.Settings.ScaleMinLabel);
        Assert.Null(s.Rating.Settings.ScaleMaxLabel);
        Assert.Equal("you@example.com", s.Email.Settings.Placeholder);
    }

    [Fact]
    public void Conditions_on_non_choice_sources_drop_the_option_id()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetQuestionId = s.Email.Id,
            Conditions = [new LogicConditionDto { SourceQuestionId = s.Rating.Id, Operator = ConditionOperator.GreaterThan, Value = " 3 ", OptionId = s.Yes.Id }],
        });

        SurveyNormalizer.Normalize(s.Definition);

        var condition = s.Definition.LogicRules[1].Conditions[0];
        Assert.Null(condition.OptionId);
        Assert.Equal("3", condition.Value);
    }

    [Fact]
    public void Conditions_with_unknown_sources_are_left_for_validation()
    {
        var s = SampleSurveys.CustomerFeedback();
        var unknown = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        s.WhyNotRule.Conditions[0].SourceQuestionId = unknown;
        s.WhyNotRule.Conditions[0].OptionId = optionId;

        SurveyNormalizer.Normalize(s.Definition);

        Assert.Equal(unknown, s.WhyNotRule.Conditions[0].SourceQuestionId);
        Assert.Equal(optionId, s.WhyNotRule.Conditions[0].OptionId);
    }
}
