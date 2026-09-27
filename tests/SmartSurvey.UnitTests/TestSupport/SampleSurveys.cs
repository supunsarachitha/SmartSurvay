using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.UnitTests.TestSupport;

/// <summary>
/// Ready-made survey definitions for tests. Ids are fresh GUIDs per call; use the returned
/// <see cref="SampleSurvey"/> handles to reference questions/options.
/// </summary>
public static class SampleSurveys
{
    /// <summary>
    /// Two-page "Customer feedback" survey:
    /// <list type="bullet">
    /// <item>Page 1: Q1 Radio "Did you enjoy the product?" (Yes / No), required;
    /// Q2 Checkbox "Which features do you use?" (Reports, Logic, Other → free text);
    /// Q3 LongText "Why not?" shown only when Q1 = No (logic rule).</item>
    /// <item>Page 2: Q4 Rating (1..5), required; Q5 Email optional; Q6 Number 0..120;
    /// Q7 Dropdown (Europe / Asia / Other → free text).</item>
    /// </list>
    /// </summary>
    public static SampleSurvey CustomerFeedback(string title = "Customer feedback")
    {
        var s = new SampleSurvey();
        s.Yes = new OptionDto { Text = "Yes", Order = 0 };
        s.No = new OptionDto { Text = "No", Order = 1 };
        s.Enjoy = new QuestionDto
        {
            Type = QuestionType.Radio, Text = "Did you enjoy the product?", Code = "Q1", Order = 0, IsRequired = true,
            Options = [s.Yes, s.No],
        };

        s.Reports = new OptionDto { Text = "Reports", Order = 0 };
        s.Logic = new OptionDto { Text = "Conditional logic", Order = 1 };
        s.OtherFeature = new OptionDto { Text = "Other", Order = 2, AllowsFreeText = true, FreeTextPlaceholder = "Please specify" };
        s.Features = new QuestionDto
        {
            Type = QuestionType.Checkbox, Text = "Which features do you use?", Code = "Q2", Order = 1,
            Options = [s.Reports, s.Logic, s.OtherFeature],
        };

        s.WhyNot = new QuestionDto { Type = QuestionType.LongText, Text = "Why not?", Code = "Q3", Order = 2, IsRequired = true };

        s.Rating = new QuestionDto { Type = QuestionType.Rating, Text = "Rate us", Code = "Q4", Order = 0, IsRequired = true };
        s.Rating.Settings.RatingMax = 5;
        s.Email = new QuestionDto { Type = QuestionType.Email, Text = "Your e-mail", Code = "Q5", Order = 1 };
        s.Age = new QuestionDto { Type = QuestionType.Number, Text = "Your age", Code = "Q6", Order = 2 };
        s.Age.Settings.MinValue = 0;
        s.Age.Settings.MaxValue = 120;
        s.Age.Settings.AllowDecimals = false;

        s.Europe = new OptionDto { Text = "Europe", Order = 0 };
        s.Asia = new OptionDto { Text = "Asia", Order = 1 };
        s.OtherRegion = new OptionDto { Text = "Other", Order = 2, AllowsFreeText = true };
        s.Region = new QuestionDto
        {
            Type = QuestionType.Dropdown, Text = "Region", Code = "Q7", Order = 3,
            Options = [s.Europe, s.Asia, s.OtherRegion],
        };

        s.Page1 = new SectionDto { Title = "Experience", Order = 0, Questions = [s.Enjoy, s.Features, s.WhyNot] };
        s.Page2 = new SectionDto { Title = "About you", Order = 1, Questions = [s.Rating, s.Email, s.Age, s.Region] };

        s.WhyNotRule = new LogicRuleDto
        {
            TargetQuestionId = s.WhyNot.Id,
            Action = LogicAction.Show,
            MatchType = LogicMatchType.All,
            Conditions = [new LogicConditionDto { SourceQuestionId = s.Enjoy.Id, Operator = ConditionOperator.Equals, OptionId = s.No.Id }],
        };

        s.Definition = new SurveyDefinitionDto
        {
            Title = title,
            Description = "Tell us what you think.",
            AllowAnonymous = true,
            ThankYouMessage = "Thanks!",
            Sections = [s.Page1, s.Page2],
            LogicRules = [s.WhyNotRule],
        };

        return s;
    }
}

/// <summary>Handles to the parts of a sample survey.</summary>
public sealed class SampleSurvey
{
    public SurveyDefinitionDto Definition { get; set; } = null!;
    public SectionDto Page1 { get; set; } = null!;
    public SectionDto Page2 { get; set; } = null!;
    public QuestionDto Enjoy { get; set; } = null!;
    public OptionDto Yes { get; set; } = null!;
    public OptionDto No { get; set; } = null!;
    public QuestionDto Features { get; set; } = null!;
    public OptionDto Reports { get; set; } = null!;
    public OptionDto Logic { get; set; } = null!;
    public OptionDto OtherFeature { get; set; } = null!;
    public QuestionDto WhyNot { get; set; } = null!;
    public LogicRuleDto WhyNotRule { get; set; } = null!;
    public QuestionDto Rating { get; set; } = null!;
    public QuestionDto Email { get; set; } = null!;
    public QuestionDto Age { get; set; } = null!;
    public QuestionDto Region { get; set; } = null!;
    public OptionDto Europe { get; set; } = null!;
    public OptionDto Asia { get; set; } = null!;
    public OptionDto OtherRegion { get; set; } = null!;
}
