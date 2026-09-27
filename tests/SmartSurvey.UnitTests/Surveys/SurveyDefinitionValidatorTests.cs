using SmartSurvey.Application.Surveys;
using SmartSurvey.Application.Surveys.Validation;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary>
/// Rules of <see cref="SurveyDefinitionValidator"/>. Paths in the sample survey:
/// Sections[0] = Experience (Q1 radio, Q2 checkbox, Q3 long text shown when Q1 = No);
/// Sections[1] = About you (Q4 rating, Q5 e-mail, Q6 number, Q7 dropdown).
/// </summary>
public class SurveyDefinitionValidatorTests
{
    private const string Q1 = "Sections[0].Questions[0]";
    private const string Q2 = "Sections[0].Questions[1]";
    private const string Q3 = "Sections[0].Questions[2]";
    private const string Q4 = "Sections[1].Questions[0]";
    private const string Q5 = "Sections[1].Questions[1]";
    private const string Q6 = "Sections[1].Questions[2]";

    private static readonly SurveyDefinitionValidator Validator = new();

    private static Dictionary<string, string[]> Validate(SurveyDefinitionDto survey) =>
        Validator.Validate(survey).Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

    private static string AssertError(SurveyDefinitionDto survey, string key)
    {
        var errors = Validate(survey);
        Assert.True(errors.ContainsKey(key), $"Expected an error for '{key}' but got: [{string.Join(", ", errors.Keys)}]");
        return errors[key][0];
    }

    private static void AssertValid(SurveyDefinitionDto survey)
    {
        var errors = Validate(survey);
        Assert.True(errors.Count == 0, "Unexpected errors: " + string.Join("; ", errors.Select(e => $"{e.Key}: {e.Value[0]}")));
    }

    private static LogicRuleDto Rule(Guid? targetQuestion, Guid? targetSection, params LogicConditionDto[] conditions) => new()
    {
        TargetQuestionId = targetQuestion,
        TargetSectionId = targetSection,
        Conditions = conditions.ToList(),
    };

    private static LogicConditionDto Condition(QuestionDto source, ConditionOperator op, Guid? optionId = null, string? value = null) => new()
    {
        SourceQuestionId = source.Id,
        Operator = op,
        OptionId = optionId,
        Value = value,
    };

    [Fact]
    public void Sample_survey_is_valid()
    {
        AssertValid(SampleSurveys.CustomerFeedback().Definition);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_is_required(string title)
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.Title = title;

        Assert.Equal("Please enter a title for the survey.", AssertError(s.Definition, "Title"));
    }

    [Fact]
    public void Title_is_limited_to_200_characters()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.Title = new string('a', 200);
        AssertValid(s.Definition);

        s.Definition.Title = new string('a', 201);
        AssertError(s.Definition, "Title");
    }

    [Theory]
    [InlineData("Description")]
    [InlineData("WelcomeMessage")]
    [InlineData("ThankYouMessage")]
    public void Long_texts_are_limited_to_4000_characters(string property)
    {
        var s = SampleSurveys.CustomerFeedback();
        typeof(SurveyDefinitionDto).GetProperty(property)!.SetValue(s.Definition, new string('x', 4001));

        AssertError(s.Definition, property);
    }

    [Fact]
    public void Opening_date_must_be_before_closing_date()
    {
        var s = SampleSurveys.CustomerFeedback();
        var date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        s.Definition.OpensAt = date;
        s.Definition.ClosesAt = date;

        Assert.Equal("The closing date must be after the opening date.", AssertError(s.Definition, "ClosesAt"));

        s.Definition.ClosesAt = date.AddDays(1);
        AssertValid(s.Definition);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Response_limit_must_be_positive(int limit)
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.MaxResponses = limit;

        AssertError(s.Definition, "MaxResponses");
    }

    [Fact]
    public void Response_limit_is_optional()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.MaxResponses = null;
        AssertValid(s.Definition);

        s.Definition.MaxResponses = 1;
        AssertValid(s.Definition);
    }

    [Theory]
    [InlineData("Has Spaces")]
    [InlineData("UPPER")]
    [InlineData("double--hyphen")]
    [InlineData("-leading")]
    public void Malformed_slug_is_rejected(string slug)
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.Slug = slug;

        Assert.Contains("lowercase letters", AssertError(s.Definition, "Slug"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("customer-feedback-2026")]
    public void Empty_or_well_formed_slug_is_accepted(string? slug)
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.Slug = slug;

        AssertValid(s.Definition);
    }

    [Fact]
    public void Survey_needs_at_least_one_page()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.Sections.Clear();
        s.Definition.LogicRules.Clear();

        Assert.Equal("The survey needs at least one page.", AssertError(s.Definition, "Sections"));
    }

    [Fact]
    public void Page_title_is_required_and_limited()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Page2.Title = " ";
        AssertError(s.Definition, "Sections[1].Title");

        s.Page2.Title = new string('p', 201);
        AssertError(s.Definition, "Sections[1].Title");

        s.Page2.Title = "Ok";
        s.Page2.Description = new string('d', 2001);
        AssertError(s.Definition, "Sections[1].Description");
    }

    [Fact]
    public void Question_text_is_required_and_limited()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Email.Text = "";
        Assert.Equal("Please enter the question text.", AssertError(s.Definition, $"{Q5}.Text"));

        s.Email.Text = new string('q', 1001);
        AssertError(s.Definition, $"{Q5}.Text");

        s.Email.Text = "E-mail";
        s.Email.Description = new string('h', 2001);
        AssertError(s.Definition, $"{Q5}.Description");
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("Q 1")]
    [InlineData("Q#1")]
    [InlineData("_q")]
    public void Question_code_must_start_with_a_letter_and_use_safe_characters(string code)
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Email.Code = code;

        Assert.Contains("must start with a letter", AssertError(s.Definition, $"{Q5}.Code"));
    }

    [Fact]
    public void Question_code_is_limited_to_50_characters_and_optional()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Email.Code = "E" + new string('x', 50);
        AssertError(s.Definition, $"{Q5}.Code");

        s.Email.Code = null;
        AssertValid(s.Definition);
    }

    [Fact]
    public void Question_codes_must_be_unique_ignoring_case()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Email.Code = "q1";

        Assert.Contains("already used", AssertError(s.Definition, $"{Q5}.Code"));
    }

    [Theory]
    [InlineData(QuestionType.Radio)]
    [InlineData(QuestionType.Checkbox)]
    [InlineData(QuestionType.Dropdown)]
    public void Choice_questions_need_at_least_two_options(QuestionType type)
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Enjoy.Type = type;
        s.Enjoy.Options.RemoveAt(1);
        s.Definition.LogicRules.Clear();

        Assert.Equal("Add at least 2 answer options.", AssertError(s.Definition, $"{Q1}.Options"));
    }

    [Fact]
    public void Option_fields_are_validated()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.No.Text = " ";
        s.Yes.Value = new string('v', 101);
        s.OtherFeature.FreeTextPlaceholder = new string('p', 201);

        var errors = Validate(s.Definition);

        Assert.Contains($"{Q1}.Options[1].Text", errors.Keys);
        Assert.Contains($"{Q1}.Options[0].Value", errors.Keys);
        Assert.Contains($"{Q2}.Options[2].FreeTextPlaceholder", errors.Keys);
    }

    [Fact]
    public void Text_length_settings_must_be_consistent()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNot.Settings.MinLength = 50;
        s.WhyNot.Settings.MaxLength = 10;
        AssertError(s.Definition, $"{Q3}.Settings.MaxLength");

        s.WhyNot.Settings.MinLength = null;
        s.WhyNot.Settings.MaxLength = 10_001;
        AssertError(s.Definition, $"{Q3}.Settings.MaxLength");

        s.WhyNot.Settings.MaxLength = 10_000;
        s.WhyNot.Settings.MinLength = -1;
        AssertError(s.Definition, $"{Q3}.Settings.MinLength");
    }

    [Fact]
    public void Number_range_must_be_consistent()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Age.Settings.MinValue = 10;
        s.Age.Settings.MaxValue = 5;

        AssertError(s.Definition, $"{Q6}.Settings.MaxValue");
    }

    [Fact]
    public void Checkbox_selection_limits_must_fit_the_options()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Features.Settings.MinSelections = 3;
        s.Features.Settings.MaxSelections = 2;
        Assert.Contains("greater than or equal to the minimum", AssertError(s.Definition, $"{Q2}.Settings.MaxSelections"));

        s.Features.Settings.MinSelections = null;
        s.Features.Settings.MaxSelections = 4; // only three options
        Assert.Contains("larger than the number of options", AssertError(s.Definition, $"{Q2}.Settings.MaxSelections"));

        s.Features.Settings.MaxSelections = null;
        s.Features.Settings.MinSelections = 4;
        AssertError(s.Definition, $"{Q2}.Settings.MinSelections");

        s.Features.Settings.MinSelections = 1;
        s.Features.Settings.MaxSelections = 3;
        AssertValid(s.Definition);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void Rating_needs_between_2_and_10_stars(int stars, bool valid)
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Rating.Settings.RatingMax = stars;

        if (valid)
        {
            AssertValid(s.Definition);
        }
        else
        {
            AssertError(s.Definition, $"{Q4}.Settings.RatingMax");
        }
    }

    [Theory]
    [InlineData(0, 10, null)]
    [InlineData(1, 5, null)]
    [InlineData(0, 20, null)]
    [InlineData(5, 5, "Settings.ScaleMax")]
    [InlineData(6, 5, "Settings.ScaleMax")]
    [InlineData(0, 21, "Settings.ScaleMax")]
    [InlineData(-1, 5, "Settings.ScaleMin")]
    [InlineData(90, 101, "Settings.ScaleMax")]
    public void Scale_range_must_be_sane(int min, int max, string? errorKey)
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Rating.Type = QuestionType.Scale;
        s.Rating.Settings.ScaleMin = min;
        s.Rating.Settings.ScaleMax = max;

        if (errorKey is null)
        {
            AssertValid(s.Definition);
        }
        else
        {
            AssertError(s.Definition, $"{Q4}.{errorKey}");
        }
    }

    [Fact]
    public void Date_range_must_be_consistent()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Email.Type = QuestionType.Date;
        s.Email.Settings.MinDate = new DateOnly(2026, 5, 1);
        s.Email.Settings.MaxDate = new DateOnly(2026, 4, 1);

        AssertError(s.Definition, $"{Q5}.Settings.MaxDate");
    }

    [Fact]
    public void Settings_of_other_question_types_are_ignored()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Email.Settings.RatingMax = 99;
        s.Email.Settings.ScaleMin = 50;
        s.Email.Settings.ScaleMax = 1;
        s.Email.Settings.MinValue = 10;
        s.Email.Settings.MaxValue = 1;

        AssertValid(s.Definition);
    }

    [Fact]
    public void Ids_must_be_unique_across_the_whole_survey()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Email.Id = s.Yes.Id; // a question reusing an option id

        Assert.Contains("same id", AssertError(s.Definition, $"{Q5}.Id"));
    }

    [Fact]
    public void Empty_ids_are_accepted_because_the_server_assigns_them()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Email.Id = Guid.Empty;
        s.Age.Id = Guid.Empty;
        s.WhyNotRule.Id = Guid.Empty;

        AssertValid(s.Definition);
    }

    [Fact]
    public void Rule_needs_exactly_one_target()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.TargetQuestionId = null;
        Assert.Contains("a question or a page", AssertError(s.Definition, "LogicRules[0]"));

        s.WhyNotRule.TargetQuestionId = s.WhyNot.Id;
        s.WhyNotRule.TargetSectionId = s.Page2.Id;
        Assert.Contains("not both", AssertError(s.Definition, "LogicRules[0]"));
    }

    [Fact]
    public void Rule_target_must_exist()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.TargetQuestionId = Guid.NewGuid();
        AssertError(s.Definition, "LogicRules[0].TargetQuestionId");

        s.WhyNotRule.TargetQuestionId = null;
        s.WhyNotRule.TargetSectionId = Guid.NewGuid();
        AssertError(s.Definition, "LogicRules[0].TargetSectionId");
    }

    [Fact]
    public void Rule_needs_at_least_one_condition()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.Conditions.Clear();

        Assert.Equal("Add at least one condition to the rule.", AssertError(s.Definition, "LogicRules[0].Conditions"));
    }

    [Fact]
    public void Condition_source_must_exist()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.Conditions[0].SourceQuestionId = Guid.NewGuid();

        AssertError(s.Definition, "LogicRules[0].Conditions[0].SourceQuestionId");
    }

    [Fact]
    public void Question_rule_source_must_come_before_the_target()
    {
        var s = SampleSurveys.CustomerFeedback();
        // Show Q1 depending on Q2 (Q2 comes after Q1).
        s.Definition.LogicRules.Add(Rule(s.Enjoy.Id, null, Condition(s.Features, ConditionOperator.Contains, s.Reports.Id)));

        var message = AssertError(s.Definition, "LogicRules[1].Conditions[0].SourceQuestionId");
        Assert.Contains("Move Q2 above Q1", message);
    }

    [Fact]
    public void Question_cannot_control_its_own_visibility()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.LogicRules.Add(Rule(s.Enjoy.Id, null, Condition(s.Enjoy, ConditionOperator.IsAnswered)));

        Assert.Contains("cannot show or hide itself", AssertError(s.Definition, "LogicRules[1].Conditions[0].SourceQuestionId"));
    }

    [Fact]
    public void Page_rule_source_must_be_on_an_earlier_page()
    {
        var s = SampleSurveys.CustomerFeedback();

        // Same page.
        s.Definition.LogicRules.Add(Rule(null, s.Page2.Id, Condition(s.Rating, ConditionOperator.GreaterThan, value: "3")));
        Assert.Contains("its own questions", AssertError(s.Definition, "LogicRules[1].Conditions[0].SourceQuestionId"));

        // Later page.
        s.Definition.LogicRules[1] = Rule(null, s.Page1.Id, Condition(s.Rating, ConditionOperator.GreaterThan, value: "3"));
        Assert.Contains("earlier pages", AssertError(s.Definition, "LogicRules[1].Conditions[0].SourceQuestionId"));

        // Earlier page: valid.
        s.Definition.LogicRules[1] = Rule(null, s.Page2.Id, Condition(s.Enjoy, ConditionOperator.Equals, s.Yes.Id));
        AssertValid(s.Definition);
    }

    [Fact]
    public void Ordering_uses_order_values_not_list_positions()
    {
        var s = SampleSurveys.CustomerFeedback();
        // Swap the display order of the pages without reordering the list: page 2 now comes first,
        // so the existing rule on page 1 is fine but a page-2 rule depending on Q1 is not.
        s.Page1.Order = 1;
        s.Page2.Order = 0;
        s.Definition.LogicRules.Add(Rule(null, s.Page2.Id, Condition(s.Enjoy, ConditionOperator.Equals, s.Yes.Id)));

        AssertError(s.Definition, "LogicRules[1].Conditions[0].SourceQuestionId");
    }

    [Fact]
    public void Operator_must_suit_the_source_question_type()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.Conditions[0].Operator = ConditionOperator.GreaterThan;

        var message = AssertError(s.Definition, "LogicRules[0].Conditions[0].Operator");
        Assert.Contains("greater than", message);
        Assert.Contains("Single choice (radio)", message);
    }

    [Fact]
    public void Choice_condition_needs_an_option_of_its_source_question()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.Conditions[0].OptionId = null;
        AssertError(s.Definition, "LogicRules[0].Conditions[0].OptionId");

        s.WhyNotRule.Conditions[0].OptionId = s.Reports.Id; // option of Q2, not of Q1
        Assert.Contains("does not belong to Q1", AssertError(s.Definition, "LogicRules[0].Conditions[0].OptionId"));
    }

    [Theory]
    [InlineData(QuestionType.Number, "abc", false)]
    [InlineData(QuestionType.Number, "3.5", true)]
    [InlineData(QuestionType.Rating, "", false)]
    [InlineData(QuestionType.Date, "31/12/2026", false)]
    [InlineData(QuestionType.Date, "2026-12-31", true)]
    [InlineData(QuestionType.ShortText, "  ", false)]
    [InlineData(QuestionType.ShortText, "hello", true)]
    public void Comparison_value_must_suit_the_source_type(QuestionType type, string value, bool valid)
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Rating.Type = type;
        s.Definition.LogicRules.Add(Rule(s.Email.Id, null, Condition(s.Rating, ConditionOperator.Equals, value: value)));

        if (valid)
        {
            AssertValid(s.Definition);
        }
        else
        {
            AssertError(s.Definition, "LogicRules[1].Conditions[0].Value");
        }
    }

    [Fact]
    public void Unary_operators_need_no_operand()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.LogicRules.Add(Rule(s.Email.Id, null,
            Condition(s.Rating, ConditionOperator.IsAnswered),
            Condition(s.Enjoy, ConditionOperator.IsNotAnswered)));

        AssertValid(s.Definition);
    }

    [Fact]
    public void Null_collections_do_not_crash_the_validator()
    {
        var survey = new SurveyDefinitionDto { Title = "T", Sections = null!, LogicRules = null! };

        var errors = Validate(survey);

        Assert.Contains("Sections", errors.Keys);
    }
}
