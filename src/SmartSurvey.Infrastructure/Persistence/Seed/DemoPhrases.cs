namespace SmartSurvey.Infrastructure.Persistence.Seed;

/// <summary>Small phrase lists used to generate realistic free-text answers for the demo responses.</summary>
internal static class DemoPhrases
{
    /// <summary>Answers to "What could we improve?" (dissatisfied customers).</summary>
    public static readonly IReadOnlyList<string> Improvements =
    [
        "Support took three days to answer my ticket. A faster first response would make a big difference.",
        "The checkout process has too many steps and forgot my address twice.",
        "Pricing is confusing – I could not tell which plan includes which features.",
        "The mobile app crashes when I upload attachments larger than a few megabytes.",
        "My last two deliveries arrived late and nobody told me in advance.",
        "I had to explain the same problem to three different agents.",
        "The documentation is out of date for the latest version.",
        "A billing error took weeks to resolve and I had to chase it myself.",
        "Too many features are hidden behind the most expensive plan.",
        "The new dashboard is slower than the old one, especially with larger accounts.",
    ];

    /// <summary>Answers to "Which feature would you like us to add next?".</summary>
    public static readonly IReadOnlyList<string> WishedFeatures =
    [
        "Dark mode",
        "Offline mode for the mobile app",
        "Scheduled PDF reports",
        "Two-factor authentication with an authenticator app",
        "Calendar integration",
        "Bulk editing",
        "Custom dashboards",
        "Slack notifications",
        "Multi-currency invoices",
        "Webhooks in the public API",
    ];

    /// <summary>Closing comments of satisfied customers.</summary>
    public static readonly IReadOnlyList<string> PositiveComments =
    [
        "Keep up the great work!",
        "The support team is fantastic – thank you, Maria!",
        "Really happy with the product so far. Onboarding was quick and painless.",
        "Best tool we have adopted this year. The whole team uses it daily.",
        "Great value for money.",
        "Everything just works. Please don't change the things that make it simple.",
    ];

    /// <summary>Closing comments of neutral customers.</summary>
    public static readonly IReadOnlyList<string> NeutralComments =
    [
        "Overall fine, but the interface could feel more modern.",
        "It does what we need. Some reports take a while to load.",
        "Good product, but the price increase this year was hard to justify.",
        "Would love more tutorials for advanced features.",
    ];

    /// <summary>Closing comments of dissatisfied customers.</summary>
    public static readonly IReadOnlyList<string> NegativeComments =
    [
        "We are evaluating alternatives because of the recent outages.",
        "Please fix the basics before adding new features.",
        "I expected more for the price we pay.",
        "Communication about changes and downtime needs to improve.",
    ];

    /// <summary>Free texts for the "Other" region option.</summary>
    public static readonly IReadOnlyList<string> OtherRegions = ["Caribbean", "Central Asia", "Pacific Islands", "Iceland"];

    /// <summary>Free texts for the "Other" feature option.</summary>
    public static readonly IReadOnlyList<string> OtherFeatures = ["Public API", "Barcode scanning", "Team calendar", "Invoice export"];

    /// <summary>Free texts for the "Other" benefit option.</summary>
    public static readonly IReadOnlyList<string> OtherBenefits = ["Childcare support", "Gym membership", "Commuter allowance", "Sabbatical programme"];

    /// <summary>Employee suggestions for the engagement survey.</summary>
    public static readonly IReadOnlyList<string> EmployeeSuggestions =
    [
        "More transparency about company goals and how my team contributes to them.",
        "Fewer meetings on Fridays so we can focus.",
        "Clearer career paths for senior engineers.",
        "A better onboarding programme for new hires.",
        "Quiet rooms in the office for focused work.",
        "More cross-team social events.",
        "Faster laptops – builds take forever.",
        "Regular one-to-ones with my manager, not only when something goes wrong.",
    ];

    /// <summary>First names used for demo people.</summary>
    public static readonly IReadOnlyList<string> FirstNames =
    [
        "Olivia", "Liam", "Emma", "Noah", "Ava", "Lucas", "Mia", "Ethan", "Sofia", "Mateo", "Amara", "Kenji", "Priya", "Jonas", "Chloe",
    ];

    /// <summary>Last names used for demo people.</summary>
    public static readonly IReadOnlyList<string> LastNames =
    [
        "Johnson", "Schmidt", "Garcia", "Nguyen", "Okafor", "Rossi", "Kowalski", "Tanaka", "Patel", "Silva", "Andersen", "Dubois", "Kim", "Murphy", "Haddad",
    ];

    /// <summary>Browser user agents stored with the demo responses.</summary>
    public static readonly IReadOnlyList<string> UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Safari/605.1.15",
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Mobile/15E148 Safari/604.1",
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130.0",
    ];
}

/// <summary>A demo person (display name and e-mail address).</summary>
/// <param name="DisplayName">Full name.</param>
/// <param name="Email">E-mail address on the reserved <c>example.com</c> domain.</param>
internal sealed record DemoPerson(string DisplayName, string Email);

/// <summary>Deterministic demo people.</summary>
internal static class DemoPeople
{
    /// <summary>Number of demo employee accounts (respondents of the login-required pulse survey).</summary>
    public const int EmployeeCount = 45;

    /// <summary>
    /// The demo employees. Names pair every first name with three different last names, so all
    /// <see cref="EmployeeCount"/> e-mail addresses are unique.
    /// </summary>
    public static IReadOnlyList<DemoPerson> Employees()
    {
        var first = DemoPhrases.FirstNames;
        var last = DemoPhrases.LastNames;
        return Enumerable.Range(0, EmployeeCount)
            .Select(i => Person(first[i % first.Count], last[(i + (i / first.Count)) % last.Count]))
            .ToList();
    }

    /// <summary>A plausible e-mail address for an anonymous customer who chose to leave one.</summary>
    public static string RandomEmail(Random random) =>
        Person(random.Pick(DemoPhrases.FirstNames), random.Pick(DemoPhrases.LastNames)).Email;

    private static DemoPerson Person(string firstName, string lastName) =>
        new($"{firstName} {lastName}", $"{firstName}.{lastName}@example.com".ToLowerInvariant());
}
