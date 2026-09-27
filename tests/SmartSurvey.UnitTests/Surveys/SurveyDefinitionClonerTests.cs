using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary>Direct tests of <see cref="SurveyDefinitionCloner"/>.</summary>
public class SurveyDefinitionClonerTests
{
    [Fact]
    public void Copy_keeps_ids_and_is_fully_independent()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.Slug = "slug";
        s.Definition.Version = 3;

        var copy = SurveyDefinitionCloner.Copy(s.Definition);

        Assert.Equal(s.Definition.Id, copy.Id);
        Assert.Equal(s.Definition.AllItemIds(), copy.AllItemIds());
        Assert.Equal(s.Definition.StructureFingerprint(), copy.StructureFingerprint());
        Assert.Equal("slug", copy.Slug);
        Assert.Equal(3, copy.Version);

        // Mutating the copy (including the settings object) never affects the source.
        copy.Sections[1].Questions[2].Settings.MaxValue = 1;
        copy.Sections[0].Questions[0].Options[0].Text = "Changed";
        copy.LogicRules[0].Conditions.Clear();
        Assert.Equal(120, s.Age.Settings.MaxValue);
        Assert.Equal("Yes", s.Yes.Text);
        Assert.Single(s.WhyNotRule.Conditions);
    }

    [Fact]
    public void Copy_with_new_ids_replaces_every_id_and_remaps_references()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetSectionId = s.Page2.Id,
            Conditions = [new LogicConditionDto { SourceQuestionId = s.Features.Id, Operator = ConditionOperator.Contains, OptionId = s.Logic.Id }],
        });

        var copy = SurveyDefinitionCloner.CopyWithNewIds(s.Definition);

        Assert.NotEqual(s.Definition.Id, copy.Id);
        Assert.Empty(s.Definition.AllItemIds().Intersect(copy.AllItemIds()));
        Assert.Equal(s.Definition.StructureFingerprint(), copy.StructureFingerprint());

        var pageRule = copy.LogicRules[1];
        Assert.Equal(copy.Sections[1].Id, pageRule.TargetSectionId);
        Assert.Equal(copy.Sections[0].Questions[1].Id, pageRule.Conditions[0].SourceQuestionId);
        Assert.Equal(copy.Sections[0].Questions[1].Options[1].Id, pageRule.Conditions[0].OptionId);
    }

    [Fact]
    public void References_outside_the_design_are_kept_so_validation_can_report_them()
    {
        var s = SampleSurveys.CustomerFeedback();
        var dangling = Guid.NewGuid();
        s.WhyNotRule.TargetQuestionId = dangling;

        var copy = SurveyDefinitionCloner.CopyWithNewIds(s.Definition);

        Assert.Equal(dangling, copy.LogicRules[0].TargetQuestionId);
    }

    [Fact]
    public void Duplicate_ids_stay_duplicate_and_empty_ids_get_fresh_ones()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Email.Id = s.Age.Id;
        s.Rating.Id = Guid.Empty;
        s.Region.Id = Guid.Empty;

        var copy = SurveyDefinitionCloner.CopyWithNewIds(s.Definition);

        var page2 = copy.Sections[1].Questions;
        Assert.Equal(page2[1].Id, page2[2].Id); // still invalid, reported by the validator
        Assert.NotEqual(Guid.Empty, page2[0].Id);
        Assert.NotEqual(Guid.Empty, page2[3].Id);
        Assert.NotEqual(page2[0].Id, page2[3].Id);
    }

    [Fact]
    public void Null_collections_are_tolerated()
    {
        var survey = new SurveyDefinitionDto
        {
            Title = "T",
            Sections = [new SectionDto { Questions = [new QuestionDto { Options = null!, Settings = null! }, null!] }, null!],
            LogicRules = null!,
        };

        var copy = SurveyDefinitionCloner.CopyWithNewIds(survey);

        var question = Assert.Single(Assert.Single(copy.Sections).Questions);
        Assert.Empty(question.Options);
        Assert.NotNull(question.Settings);
        Assert.Empty(copy.LogicRules);
    }
}
