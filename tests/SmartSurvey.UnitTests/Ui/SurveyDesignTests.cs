using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Admin.Builder;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>Builder editing operations on the customer feedback sample (page 1: Q1–Q3, page 2: Q4–Q7; Q3 shown when Q1 = No).</summary>
public sealed class SurveyDesignTests
{
    private readonly SampleSurvey _s = SampleSurveys.CustomerFeedback();

    private SurveyDefinitionDto Survey => _s.Definition;

    [Fact]
    public void Placed_numbers_questions_across_pages()
    {
        var placed = SurveyDesign.Placed(Survey);

        Assert.Equal(7, placed.Count);
        Assert.Equal(_s.Rating, placed[3].Question);
        Assert.Equal(4, placed[3].Number);
        Assert.Equal(1, placed[3].SectionIndex);
    }

    [Fact]
    public void Logic_sources_are_strictly_earlier_questions()
    {
        Assert.Equal([_s.Enjoy, _s.Features], SurveyDesign.QuestionsBefore(Survey, _s.WhyNot.Id).Select(p => p.Question));
        Assert.Equal(3, SurveyDesign.QuestionsBeforeSection(Survey, _s.Page2.Id).Count);
        Assert.Empty(SurveyDesign.QuestionsBeforeSection(Survey, _s.Page1.Id));
    }

    [Theory]
    [InlineData(QuestionType.Radio, 2)]
    [InlineData(QuestionType.Checkbox, 2)]
    [InlineData(QuestionType.ShortText, 0)]
    public void New_questions_get_type_defaults(QuestionType type, int options)
    {
        var question = SurveyDesign.NewQuestion(type);

        Assert.Equal(options, question.Options.Count);
    }

    [Fact]
    public void New_rating_and_scale_questions_get_valid_ranges()
    {
        Assert.Equal(5, SurveyDesign.NewQuestion(QuestionType.Rating).Settings.RatingMax);
        var scale = SurveyDesign.NewQuestion(QuestionType.Scale).Settings;
        Assert.True(scale.ScaleMax > scale.ScaleMin);
    }

    [Fact]
    public void Add_question_inserts_after_the_given_question_and_renumbers()
    {
        var added = SurveyDesign.AddQuestion(_s.Page1, QuestionType.Number, after: _s.Enjoy);

        Assert.Equal([_s.Enjoy, added, _s.Features, _s.WhyNot], _s.Page1.Questions);
        Assert.Equal([0, 1, 2, 3], _s.Page1.Questions.Select(q => q.Order));
    }

    [Fact]
    public void Duplicate_copies_with_new_ids_below_the_original()
    {
        var copy = SurveyDesign.DuplicateQuestion(_s.Page1, _s.Features);

        Assert.Equal(2, _s.Page1.Questions.IndexOf(copy));
        Assert.NotEqual(_s.Features.Id, copy.Id);
        Assert.Equal("Which features do you use? (copy)", copy.Text);
        Assert.Equal(_s.Features.Options.Select(o => o.Text), copy.Options.Select(o => o.Text));
        Assert.Empty(copy.Options.Select(o => o.Id).Intersect(_s.Features.Options.Select(o => o.Id)));
        Assert.True(copy.Options.Single(o => o.Text == "Other").AllowsFreeText);
    }

    [Fact]
    public void Moving_past_the_end_of_a_page_moves_to_the_next_page()
    {
        Assert.True(SurveyDesign.MoveQuestion(Survey, _s.WhyNot, 1));

        Assert.DoesNotContain(_s.WhyNot, _s.Page1.Questions);
        Assert.Equal(_s.WhyNot, _s.Page2.Questions[0]);
        Assert.Equal([0, 1, 2, 3, 4], _s.Page2.Questions.Select(q => q.Order));
    }

    [Fact]
    public void Moving_up_from_the_top_of_a_page_moves_to_the_end_of_the_previous_page()
    {
        Assert.True(SurveyDesign.MoveQuestion(Survey, _s.Rating, -1));

        Assert.Equal(_s.Rating, _s.Page1.Questions[^1]);
    }

    [Fact]
    public void The_first_and_last_questions_cannot_move_further()
    {
        Assert.False(SurveyDesign.MoveQuestion(Survey, _s.Enjoy, -1));
        Assert.False(SurveyDesign.MoveQuestion(Survey, _s.Region, 1));
    }

    [Fact]
    public void Deleting_a_question_removes_rules_that_target_or_use_it()
    {
        SurveyDesign.DeleteQuestion(Survey, _s.Enjoy);

        Assert.DoesNotContain(_s.Enjoy, _s.Page1.Questions);
        Assert.Empty(Survey.LogicRules); // the only rule's condition used Q1
    }

    [Fact]
    public void Deleting_a_rule_target_removes_the_rule()
    {
        SurveyDesign.DeleteQuestion(Survey, _s.WhyNot);

        Assert.Empty(Survey.LogicRules);
    }

    [Fact]
    public void Deleting_an_option_removes_conditions_that_compare_with_it()
    {
        SurveyDesign.DeleteOption(Survey, _s.Enjoy, _s.No);

        Assert.Equal([_s.Yes], _s.Enjoy.Options);
        Assert.Empty(Survey.LogicRules);
    }

    [Fact]
    public void Deleting_a_page_removes_its_questions_and_page_rules()
    {
        Survey.LogicRules.Add(new LogicRuleDto
        {
            TargetSectionId = _s.Page2.Id,
            Conditions = [new LogicConditionDto { SourceQuestionId = _s.Enjoy.Id, Operator = ConditionOperator.IsAnswered }],
        });

        SurveyDesign.DeleteSection(Survey, _s.Page2);

        Assert.Single(Survey.Sections);
        Assert.DoesNotContain(Survey.LogicRules, r => r.TargetSectionId == _s.Page2.Id);
        Assert.Single(Survey.LogicRules); // the Q3 rule is unaffected
    }

    [Fact]
    public void New_options_stay_above_other_options_with_free_text()
    {
        var added = SurveyDesign.AddOption(_s.Features);

        Assert.Equal(["Reports", "Conditional logic", "Option 4", "Other"], _s.Features.Options.Select(o => o.Text));
        Assert.Equal(2, added.Order);
    }

    [Fact]
    public void Adding_a_page_appends_one_with_a_blank_question()
    {
        var page = SurveyDesign.AddSection(Survey);

        Assert.Equal("Page 3", page.Title);
        Assert.Equal(2, page.Order);
        Assert.Single(page.Questions);
    }

    [Fact]
    public void Condition_helpers_follow_the_source_question_type()
    {
        var condition = SurveyDesign.NewCondition(_s.Features);

        Assert.Equal(ConditionOperator.Contains, condition.Operator);
        Assert.Equal(_s.Reports.Id, condition.OptionId);
        Assert.True(SurveyDesign.NeedsOption(QuestionType.Checkbox, ConditionOperator.Contains));
        Assert.False(SurveyDesign.NeedsOption(QuestionType.Checkbox, ConditionOperator.IsAnswered));
        Assert.True(SurveyDesign.NeedsValue(QuestionType.Number, ConditionOperator.GreaterThan));
        Assert.Equal("≥", SurveyDesign.OperatorLabel(QuestionType.Number, ConditionOperator.GreaterThanOrEqual));
        Assert.Equal("is after", SurveyDesign.OperatorLabel(QuestionType.Date, ConditionOperator.GreaterThan));
        Assert.Equal("includes", SurveyDesign.OperatorLabel(QuestionType.Checkbox, ConditionOperator.Contains));

        SurveyDesign.ResetCondition(condition, _s.Age);
        Assert.Equal(ConditionOperator.Equals, condition.Operator);
        Assert.Null(condition.OptionId);
    }
}
