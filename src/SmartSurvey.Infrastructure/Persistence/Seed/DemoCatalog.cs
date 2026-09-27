namespace SmartSurvey.Infrastructure.Persistence.Seed;

/// <summary>
/// Question codes and answer labels of the demo "Customer Satisfaction Survey". Shared by the survey
/// factory, the response generator and the sample report so the three always agree.
/// </summary>
internal static class CustomerSurveyCatalog
{
    /// <summary>Survey title.</summary>
    public const string Title = "Customer Satisfaction Survey";

    /// <summary>Share-link slug.</summary>
    public const string Slug = "customer-satisfaction-survey";

    /// <summary>Page 1 · Radio: role (drives the "company size" hide rule).</summary>
    public const string Role = "Q1";

    /// <summary>Page 1 · Dropdown: company size (hidden for students).</summary>
    public const string CompanySize = "Q2";

    /// <summary>Page 1 · Dropdown with "Other": region.</summary>
    public const string Region = "Q3";

    /// <summary>Page 1 · E-mail (optional).</summary>
    public const string Email = "Q4";

    /// <summary>Page 2 · Radio: overall satisfaction.</summary>
    public const string Satisfaction = "Q5";

    /// <summary>Page 2 · Long text: improvement suggestion (shown only to dissatisfied customers).</summary>
    public const string Improvement = "Q6";

    /// <summary>Page 2 · Star rating of the support team.</summary>
    public const string SupportRating = "Q7";

    /// <summary>Page 2 · NPS scale 0–10.</summary>
    public const string Recommend = "Q8";

    /// <summary>Page 2 · Radio: product used? (drives the "Product usage" page rule).</summary>
    public const string UsedProduct = "Q9";

    /// <summary>Page 2 · Date: customer since.</summary>
    public const string CustomerSince = "Q10";

    /// <summary>Page 3 · Checkbox with "Other": features used.</summary>
    public const string Features = "Q11";

    /// <summary>Page 3 · Number: hours of use per week.</summary>
    public const string HoursPerWeek = "Q12";

    /// <summary>Page 3 · Short text: most wanted feature.</summary>
    public const string WishedFeature = "Q13";

    /// <summary>Page 3 · Long text: other comments.</summary>
    public const string Comments = "Q14";

    /// <summary>Role options.</summary>
    public static readonly IReadOnlyList<string> Roles = ["Business owner", "Manager", "Individual contributor", "Freelancer", "Student"];

    /// <summary>Role that hides the company-size question.</summary>
    public const string StudentRole = "Student";

    /// <summary>Company-size options.</summary>
    public static readonly IReadOnlyList<string> CompanySizes =
        ["1–10 employees", "11–50 employees", "51–200 employees", "201–1,000 employees", "More than 1,000 employees"];

    /// <summary>Region options (the "Other" option is appended separately).</summary>
    public static readonly IReadOnlyList<string> Regions = ["North America", "Europe", "Asia-Pacific", "Latin America", "Middle East & Africa"];

    /// <summary>Satisfaction options from best to worst.</summary>
    public static readonly IReadOnlyList<string> SatisfactionLevels = ["Very satisfied", "Satisfied", "Neutral", "Dissatisfied", "Very dissatisfied"];

    /// <summary>Satisfaction option that shows the improvement question.</summary>
    public const string Dissatisfied = "Dissatisfied";

    /// <summary>Satisfaction option that shows the improvement question.</summary>
    public const string VeryDissatisfied = "Very dissatisfied";

    /// <summary>Feature options (the "Other" option is appended separately).</summary>
    public static readonly IReadOnlyList<string> FeatureOptions = ["Online ordering", "Mobile app", "Live chat support", "Reports & analytics", "Integrations"];

    /// <summary>Label of the combined "choice + free text" options.</summary>
    public const string Other = "Other (please specify)";

    /// <summary>"Yes" option.</summary>
    public const string Yes = "Yes";

    /// <summary>"No" option.</summary>
    public const string No = "No";
}

/// <summary>Question codes and answer labels of the demo "Employee Engagement Pulse".</summary>
internal static class EngagementSurveyCatalog
{
    /// <summary>Survey title.</summary>
    public const string Title = "Employee Engagement Pulse";

    /// <summary>Share-link slug.</summary>
    public const string Slug = "employee-engagement-pulse";

    /// <summary>Page 1 · Dropdown: department.</summary>
    public const string Department = "Q1";

    /// <summary>Page 1 · Likert: feeling valued.</summary>
    public const string Valued = "Q2";

    /// <summary>Page 1 · Likert: tools and resources.</summary>
    public const string Tools = "Q3";

    /// <summary>Page 1 · Likert: would recommend the employer.</summary>
    public const string Recommend = "Q4";

    /// <summary>Page 1 · Likert: manager feedback.</summary>
    public const string Feedback = "Q5";

    /// <summary>Page 2 · Checkbox with "Other": most valued benefits (max 3).</summary>
    public const string Benefits = "Q6";

    /// <summary>Page 2 · Star rating: work-life balance.</summary>
    public const string WorkLifeBalance = "Q7";

    /// <summary>Page 2 · Long text: suggestion (optional).</summary>
    public const string Suggestion = "Q8";

    /// <summary>Codes of the Likert statements in display order.</summary>
    public static readonly IReadOnlyList<string> LikertCodes = [Valued, Tools, Recommend, Feedback];

    /// <summary>Likert options from most to least positive.</summary>
    public static readonly IReadOnlyList<string> LikertLevels = ["Strongly agree", "Agree", "Neutral", "Disagree", "Strongly disagree"];

    /// <summary>Department options.</summary>
    public static readonly IReadOnlyList<string> Departments =
        ["Engineering", "Sales", "Marketing", "Customer Support", "Finance", "Human Resources", "Operations"];

    /// <summary>Benefit options (the "Other" option is appended separately).</summary>
    public static readonly IReadOnlyList<string> BenefitOptions =
        ["Flexible working hours", "Remote work", "Health insurance", "Learning budget", "Extra vacation days"];

    /// <summary>Maximum number of benefits a respondent may pick.</summary>
    public const int MaxBenefits = 3;

    /// <summary>Label of the combined "choice + free text" option.</summary>
    public const string Other = "Other (please specify)";
}
