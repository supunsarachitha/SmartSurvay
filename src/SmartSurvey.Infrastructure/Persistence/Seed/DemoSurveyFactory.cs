using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using C = SmartSurvey.Infrastructure.Persistence.Seed.CustomerSurveyCatalog;
using E = SmartSurvey.Infrastructure.Persistence.Seed.EngagementSurveyCatalog;

namespace SmartSurvey.Infrastructure.Persistence.Seed;

/// <summary>The demo surveys created by <see cref="DemoSurveyFactory"/>.</summary>
/// <param name="Customer">Published, anonymous "Customer Satisfaction Survey" using every question type and logic.</param>
/// <param name="Engagement">Published, login-required "Employee Engagement Pulse" (one response per user).</param>
/// <param name="EventTemplate">"Event Feedback" template.</param>
/// <param name="Roadmap">"Product Roadmap Input" draft.</param>
internal sealed record DemoSurveys(Survey Customer, Survey Engagement, Survey EventTemplate, Survey Roadmap)
{
    /// <summary>All demo surveys.</summary>
    public IReadOnlyList<Survey> All => [Customer, Engagement, EventTemplate, Roadmap];
}

/// <summary>
/// Builds the demo survey designs as entity graphs (they are inserted directly, not through
/// <c>SurveyService</c>, so seeding needs no current user). Timestamps are relative to the seeding
/// time so the demo always looks recent.
/// </summary>
internal static class DemoSurveyFactory
{
    /// <summary>Creates the four demo surveys.</summary>
    /// <param name="utcNow">Seeding time (UTC).</param>
    /// <param name="createdById">Administrator recorded as creator (null when none exists).</param>
    public static DemoSurveys Create(DateTime utcNow, Guid? createdById) => new(
        CustomerSatisfaction(utcNow, createdById),
        EmployeeEngagement(utcNow, createdById),
        EventFeedbackTemplate(utcNow, createdById),
        ProductRoadmap(utcNow, createdById));

    /// <summary>
    /// Three pages using every question type, "Other → free text" options, required flags, help texts and
    /// three kinds of logic: an Any-rule showing a question, a rule showing a page and a rule hiding a question.
    /// </summary>
    private static Survey CustomerSatisfaction(DateTime utcNow, Guid? createdById)
    {
        var survey = NewSurvey(C.Title, C.Slug, SurveyStatus.Published, utcNow.AddDays(-70), createdById);
        survey.Description = "Help us understand how well we serve you. The survey takes about five minutes.";
        survey.AllowAnonymous = true;
        survey.AllowMultipleResponses = true;
        survey.PublishedAt = utcNow.AddDays(-62);
        survey.WelcomeMessage =
            "Thank you for taking a few minutes to share your experience with us. Your answers are anonymous unless you choose to leave your e-mail address.";
        survey.ThankYouMessage =
            "Thank you for your feedback! We read every response and use it to improve our products and service.";

        var b = new DemoSurveyBuilder(survey);

        b.Section("About you", "A little background helps us put your feedback in context.");
        var role = b.Choice(C.Role, QuestionType.Radio, "What best describes your role?", C.Roles, required: true);
        var companySize = b.Choice(C.CompanySize, QuestionType.Dropdown, "How large is your company?", C.CompanySizes,
            help: "Count everyone on the payroll, including part-time staff.");
        b.Choice(C.Region, QuestionType.Dropdown, "Where are you based?", C.Regions, required: true, otherOption: C.Other);
        b.Input(C.Email, QuestionType.Email, "Your e-mail address",
            help: "Optional – only if you would like us to follow up on your feedback.",
            settings: s => s.Placeholder = "name@example.com");

        b.Section("Your experience", "Tell us how we are doing.");
        var satisfaction = b.Choice(C.Satisfaction, QuestionType.Radio, "Overall, how satisfied are you with our service?",
            C.SatisfactionLevels, required: true);
        var improvement = b.Input(C.Improvement, QuestionType.LongText, "What could we improve?", required: true,
            help: "We are sorry to hear that. Please tell us what went wrong so we can fix it.",
            settings: s =>
            {
                s.MaxLength = 2000;
                s.Placeholder = "Describe what disappointed you…";
            });
        b.Input(C.SupportRating, QuestionType.Rating, "How would you rate our customer support?", required: true,
            help: "1 star = poor, 5 stars = excellent.", settings: s => s.RatingMax = 5);
        b.Input(C.Recommend, QuestionType.Scale, "How likely are you to recommend us to a friend or colleague?", required: true,
            settings: s =>
            {
                s.ScaleMin = 0;
                s.ScaleMax = 10;
                s.ScaleMinLabel = "Not at all likely";
                s.ScaleMaxLabel = "Extremely likely";
            });
        var usedProduct = b.Choice(C.UsedProduct, QuestionType.Radio, "Have you used our product?", [C.Yes, C.No], required: true,
            help: "Answer \"Yes\" to tell us which features you use on the next page.");
        b.Input(C.CustomerSince, QuestionType.Date, "When did you first become a customer?",
            help: "An approximate date is fine.", settings: s => s.MinDate = new DateOnly(2010, 1, 1));

        var usage = b.Section("Product usage", "A few questions about how you use the product.");
        b.Choice(C.Features, QuestionType.Checkbox, "Which features do you use?", C.FeatureOptions, required: true,
            help: "Select all that apply.", otherOption: C.Other, settings: s => s.MinSelections = 1);
        b.Input(C.HoursPerWeek, QuestionType.Number, "About how many hours per week do you use the product?",
            settings: s =>
            {
                s.MinValue = 0;
                s.MaxValue = 168;
                s.AllowDecimals = false;
                s.Placeholder = "e.g. 5";
            });
        b.Input(C.WishedFeature, QuestionType.ShortText, "Which feature would you like us to add next?",
            settings: s =>
            {
                s.MaxLength = 200;
                s.Placeholder = "Your wish list…";
            });
        b.Input(C.Comments, QuestionType.LongText, "Is there anything else you would like to tell us?",
            settings: s => s.MaxLength = 4000);

        // Dissatisfied customers (either of the two negative answers) are asked what to improve.
        b.QuestionRule(improvement, LogicAction.Show, LogicMatchType.Any,
            DemoSurveyBuilder.Selected(satisfaction, C.Dissatisfied),
            DemoSurveyBuilder.Selected(satisfaction, C.VeryDissatisfied));

        // The whole "Product usage" page only applies to customers who have used the product.
        b.SectionRule(usage, LogicAction.Show, LogicMatchType.All, DemoSurveyBuilder.Selected(usedProduct, C.Yes));

        // Company size is irrelevant for students.
        b.QuestionRule(companySize, LogicAction.Hide, LogicMatchType.All, DemoSurveyBuilder.Selected(role, C.StudentRole));

        return survey;
    }

    /// <summary>Login-required pulse survey with Likert statements, benefits (max 3, with "Other") and a rating.</summary>
    private static Survey EmployeeEngagement(DateTime utcNow, Guid? createdById)
    {
        var survey = NewSurvey(E.Title, E.Slug, SurveyStatus.Published, utcNow.AddDays(-50), createdById);
        survey.Description = "A two-minute quarterly check-in on how you feel about working here. Results are only reported in aggregate.";
        survey.AllowAnonymous = false;
        survey.AllowMultipleResponses = false;
        survey.PublishedAt = utcNow.AddDays(-42);
        survey.WelcomeMessage = "This pulse survey helps us understand what is going well and where we can do better. It takes about two minutes.";
        survey.ThankYouMessage = "Thanks for sharing your view! We will present the results and our next steps at the upcoming town hall.";

        var b = new DemoSurveyBuilder(survey);

        b.Section("Engagement", "How much do you agree with the following statements?");
        b.Choice(E.Department, QuestionType.Dropdown, "Which department do you work in?", E.Departments, required: true);
        b.Choice(E.Valued, QuestionType.Radio, "I feel valued for the work I do.", E.LikertLevels, required: true);
        b.Choice(E.Tools, QuestionType.Radio, "I have the tools and resources I need to do my job well.", E.LikertLevels, required: true);
        b.Choice(E.Recommend, QuestionType.Radio, "I would recommend this company as a great place to work.", E.LikertLevels, required: true);
        b.Choice(E.Feedback, QuestionType.Radio, "My manager gives me regular, useful feedback.", E.LikertLevels, required: true);

        b.Section("Wellbeing & benefits");
        b.Choice(E.Benefits, QuestionType.Checkbox, "Which benefits matter most to you?", E.BenefitOptions, required: true,
            help: $"Choose up to {E.MaxBenefits}.", otherOption: E.Other,
            settings: s =>
            {
                s.MinSelections = 1;
                s.MaxSelections = E.MaxBenefits;
            });
        b.Input(E.WorkLifeBalance, QuestionType.Rating, "How would you rate your work-life balance?", required: true,
            settings: s => s.RatingMax = 5);
        b.Input(E.Suggestion, QuestionType.LongText, "What is one thing we could do to make this a better place to work?",
            help: "Optional, but we read every suggestion.", settings: s => s.MaxLength = 2000);

        return survey;
    }

    /// <summary>Reusable template; the follow-up question is shown for ratings of 3 stars or less.</summary>
    private static Survey EventFeedbackTemplate(DateTime utcNow, Guid? createdById)
    {
        var survey = NewSurvey("Event Feedback", "event-feedback", SurveyStatus.Draft, utcNow.AddDays(-90), createdById);
        survey.IsTemplate = true;
        survey.AllowAnonymous = true;
        survey.Description = "Reusable template to collect attendee feedback after a conference, webinar or meetup.";
        survey.WelcomeMessage = "Thanks for attending! Please take two minutes to tell us about the event.";
        survey.ThankYouMessage = "Thank you – we hope to see you at our next event!";

        var b = new DemoSurveyBuilder(survey);
        b.Section("Your feedback");
        var overall = b.Input("Q1", QuestionType.Rating, "Overall, how would you rate the event?", required: true,
            settings: s => s.RatingMax = 5);
        b.Choice("Q2", QuestionType.Checkbox, "Which sessions did you attend?",
            ["Keynote", "Workshops", "Panel discussion", "Networking reception"], otherOption: C.Other);
        b.Choice("Q3", QuestionType.Radio, "How was the length of the event?", ["Too short", "About right", "Too long"], required: true);
        b.Input("Q4", QuestionType.Scale, "How likely are you to attend our next event?", required: true,
            settings: s =>
            {
                s.ScaleMin = 0;
                s.ScaleMax = 10;
                s.ScaleMinLabel = "Not at all likely";
                s.ScaleMaxLabel = "Extremely likely";
            });
        var improvements = b.Input("Q5", QuestionType.LongText, "What should we do differently next time?",
            help: "Shown when the event was rated 3 stars or less.");
        b.Input("Q6", QuestionType.Email, "Your e-mail address", help: "Optional – to hear about upcoming events.");

        b.QuestionRule(improvements, LogicAction.Show, LogicMatchType.All,
            DemoSurveyBuilder.Compare(overall, ConditionOperator.LessThanOrEqual, "3"));

        return survey;
    }

    /// <summary>Draft survey still being designed; the e-mail question appears only after opting in.</summary>
    private static Survey ProductRoadmap(DateTime utcNow, Guid? createdById)
    {
        var survey = NewSurvey("Product Roadmap Input", "product-roadmap-input", SurveyStatus.Draft, utcNow.AddDays(-3), createdById);
        survey.Description = "Help us decide what to build next quarter.";
        survey.ThankYouMessage = "Thank you! Your input goes straight to our product team.";

        var b = new DemoSurveyBuilder(survey);
        b.Section("Priorities");
        b.Choice("Q1", QuestionType.Checkbox, "Which areas should we focus on next quarter?",
            ["Performance", "Mobile experience", "Integrations", "Reporting & dashboards", "Accessibility"],
            required: true, help: "Choose up to 3.", otherOption: C.Other,
            settings: s => s.MaxSelections = 3);
        b.Choice("Q2", QuestionType.Radio, "How often do you use the product?", ["Daily", "Weekly", "Monthly", "Rarely"], required: true);
        b.Input("Q3", QuestionType.ShortText, "If you could add one feature, what would it be?", settings: s => s.MaxLength = 200);
        b.Input("Q4", QuestionType.Number, "How many people in your team use the product?",
            settings: s =>
            {
                s.MinValue = 1;
                s.MaxValue = 10_000;
                s.AllowDecimals = false;
            });

        b.Section("Stay in touch");
        var contact = b.Choice("Q5", QuestionType.Radio, "May we contact you about beta features?", [C.Yes, C.No], required: true);
        var email = b.Input("Q6", QuestionType.Email, "Your e-mail address", required: true);

        b.QuestionRule(email, LogicAction.Show, LogicMatchType.All, DemoSurveyBuilder.Selected(contact, C.Yes));

        return survey;
    }

    private static Survey NewSurvey(string title, string slug, SurveyStatus status, DateTime createdAt, Guid? createdById) => new()
    {
        Title = title,
        Slug = slug,
        Status = status,
        CreatedAt = createdAt, // explicit values are kept by the auditable-entity interceptor
        CreatedById = createdById,
        Version = 1,
        ShowProgressBar = true,
        ShowQuestionNumbers = true,
    };
}
