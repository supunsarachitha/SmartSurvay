using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using C = SmartSurvey.Infrastructure.Persistence.Seed.CustomerSurveyCatalog;
using E = SmartSurvey.Infrastructure.Persistence.Seed.EngagementSurveyCatalog;

namespace SmartSurvey.Infrastructure.Persistence.Seed;

/// <summary>
/// A simulated respondent. All random decisions are made up front (a "persona"), so answers are
/// consistent with each other — e.g. an unhappy customer gives a low NPS score and a critical comment.
/// </summary>
internal interface IDemoRespondent
{
    /// <summary>The answer to <paramref name="question"/>, or null to leave it unanswered.</summary>
    AnswerInputDto? AnswerFor(QuestionDto question);
}

/// <summary>A customer answering the "Customer Satisfaction Survey".</summary>
internal sealed class CustomerRespondent : IDemoRespondent
{
    private static readonly int[] RoleWeights = [18, 30, 32, 10, 10];
    private static readonly int[] CompanySizeWeights = [25, 30, 22, 13, 10];

    // Five named regions plus "Other" (last).
    private static readonly int[] RegionWeights = [35, 30, 18, 8, 6, 3];

    // Small per-region shift of the satisfaction score so the demo cross-tab shows real differences.
    private static readonly double[] RegionBias = [0.0, 0.06, -0.07, 0.08, -0.1, 0.0];

    // Support rating (stars) and NPS ranges per satisfaction level (very satisfied … very dissatisfied).
    private static readonly (int Min, int Max)[] RatingRange = [(4, 5), (3, 5), (2, 4), (1, 3), (1, 2)];
    private static readonly (int Min, int Max)[] NpsRange = [(8, 10), (6, 9), (4, 8), (1, 6), (0, 4)];

    private readonly int _role;
    private readonly int? _companySize;
    private readonly int _region;
    private readonly string? _otherRegion;
    private readonly string? _email;
    private readonly int _satisfaction;
    private readonly string _improvement;
    private readonly int _supportRating;
    private readonly int _nps;
    private readonly bool _usedProduct;
    private readonly DateOnly? _customerSince;
    private readonly List<string> _features;
    private readonly string? _otherFeature;
    private readonly int? _hoursPerWeek;
    private readonly string? _wishedFeature;
    private readonly string? _comment;

    /// <summary>Creates a random customer persona.</summary>
    /// <param name="random">Shared deterministic random source.</param>
    /// <param name="utcNow">Seeding time (for the "customer since" date).</param>
    /// <param name="knownEmail">E-mail of a logged-in respondent; anonymous customers leave one only sometimes.</param>
    public CustomerRespondent(Random random, DateTime utcNow, string? knownEmail)
    {
        _role = random.PickWeighted(RoleWeights);
        var isFreelancer = C.Roles[_role] == "Freelancer";
        _companySize = random.Chance(0.85) ? (isFreelancer ? 0 : random.PickWeighted(CompanySizeWeights)) : null;

        _region = random.PickWeighted(RegionWeights);
        _otherRegion = _region == C.Regions.Count ? random.Pick(DemoPhrases.OtherRegions) : null;
        _email = knownEmail ?? (random.Chance(0.25) ? DemoPeople.RandomEmail(random) : null);

        var score = Math.Clamp(random.NextDouble() + RegionBias[_region], 0, 1);
        _satisfaction = score switch
        {
            >= 0.68 => 0,
            >= 0.33 => 1,
            >= 0.17 => 2,
            >= 0.06 => 3,
            _ => 4,
        };
        _improvement = random.Pick(DemoPhrases.Improvements);
        _supportRating = random.Next(RatingRange[_satisfaction].Min, RatingRange[_satisfaction].Max + 1);
        _nps = random.Next(NpsRange[_satisfaction].Min, NpsRange[_satisfaction].Max + 1);

        _usedProduct = random.Chance(0.82);
        _customerSince = random.Chance(0.7) ? DateOnly.FromDateTime(utcNow.AddDays(-random.Next(90, 6 * 365))) : null;
        _features = random.PickDistinct(C.FeatureOptions, random.PickWeighted([30, 35, 22, 13]) + 1);
        _otherFeature = random.Chance(0.08) ? random.Pick(DemoPhrases.OtherFeatures) : null;
        _hoursPerWeek = random.Chance(0.75) ? random.Next(1, 41) : null;
        _wishedFeature = random.Chance(0.4) ? random.Pick(DemoPhrases.WishedFeatures) : null;
        _comment = random.Chance(0.3) ? random.Pick(CommentsFor(_satisfaction)) : null;
    }

    /// <inheritdoc />
    public AnswerInputDto? AnswerFor(QuestionDto question) => question.Code switch
    {
        C.Role => DemoAnswer.Option(question, C.Roles[_role]),
        C.CompanySize => _companySize is { } size ? DemoAnswer.Option(question, C.CompanySizes[size]) : null,
        C.Region => _otherRegion is null ? DemoAnswer.Option(question, C.Regions[_region]) : DemoAnswer.Other(question, _otherRegion),
        C.Email => DemoAnswer.TextOrNull(question, _email),
        C.Satisfaction => DemoAnswer.Option(question, C.SatisfactionLevels[_satisfaction]),
        C.Improvement => DemoAnswer.Text(question, _improvement),
        C.SupportRating => DemoAnswer.Number(question, _supportRating),
        C.Recommend => DemoAnswer.Number(question, _nps),
        C.UsedProduct => DemoAnswer.Option(question, _usedProduct ? C.Yes : C.No),
        C.CustomerSince => _customerSince is { } since ? DemoAnswer.Date(question, since) : null,
        C.Features => DemoAnswer.Options(question, _features, _otherFeature),
        C.HoursPerWeek => _hoursPerWeek is { } hours ? DemoAnswer.Number(question, hours) : null,
        C.WishedFeature => DemoAnswer.TextOrNull(question, _wishedFeature),
        C.Comments => DemoAnswer.TextOrNull(question, _comment),
        _ => null,
    };

    private static IReadOnlyList<string> CommentsFor(int satisfaction) => satisfaction switch
    {
        <= 1 => DemoPhrases.PositiveComments,
        2 => DemoPhrases.NeutralComments,
        _ => DemoPhrases.NegativeComments,
    };
}

/// <summary>An employee answering the "Employee Engagement Pulse".</summary>
internal sealed class EmployeeRespondent : IDemoRespondent
{
    private static readonly int[] DepartmentWeights = [30, 15, 10, 15, 8, 7, 15];

    // Likert level offset per department (positive = less engaged) for a more interesting breakdown.
    private static readonly int[] DepartmentShift = [0, 0, 0, 1, 0, -1, 1];

    private readonly int _department;
    private readonly Dictionary<string, int> _likert = [];
    private readonly List<string> _benefits;
    private readonly string? _otherBenefit;
    private readonly int _workLifeBalance;
    private readonly string? _suggestion;

    /// <summary>Creates a random employee persona.</summary>
    /// <param name="random">Shared deterministic random source.</param>
    public EmployeeRespondent(Random random)
    {
        _department = random.PickWeighted(DepartmentWeights);

        // Overall mood (0 = strongly agree … 4 = strongly disagree), then small per-statement noise.
        var mood = Math.Clamp(random.PickWeighted([30, 40, 15, 10, 5]) + DepartmentShift[_department], 0, 4);
        foreach (var code in E.LikertCodes)
        {
            var noise = random.Chance(0.4) ? random.Next(-1, 2) : 0;
            _likert[code] = Math.Clamp(mood + noise, 0, E.LikertLevels.Count - 1);
        }

        _otherBenefit = random.Chance(0.1) ? random.Pick(DemoPhrases.OtherBenefits) : null;
        var maxNamed = _otherBenefit is null ? E.MaxBenefits : E.MaxBenefits - 1;
        _benefits = random.PickDistinct(E.BenefitOptions, random.Next(1, maxNamed + 1));
        _workLifeBalance = Math.Clamp(5 - mood + random.Next(-1, 2), 1, 5);
        _suggestion = random.Chance(0.45) ? random.Pick(DemoPhrases.EmployeeSuggestions) : null;
    }

    /// <inheritdoc />
    public AnswerInputDto? AnswerFor(QuestionDto question) => question.Code switch
    {
        E.Department => DemoAnswer.Option(question, E.Departments[_department]),
        E.Benefits => DemoAnswer.Options(question, _benefits, _otherBenefit),
        E.WorkLifeBalance => DemoAnswer.Number(question, _workLifeBalance),
        E.Suggestion => DemoAnswer.TextOrNull(question, _suggestion),
        { } code when _likert.TryGetValue(code, out var level) => DemoAnswer.Option(question, E.LikertLevels[level]),
        _ => null,
    };
}

/// <summary>Builds <see cref="AnswerInputDto"/> values for a question, looking options up by label.</summary>
internal static class DemoAnswer
{
    /// <summary>Selects the option labelled <paramref name="optionText"/>.</summary>
    public static AnswerInputDto Option(QuestionDto question, string optionText) =>
        Selections(question, [new SelectionInputDto { OptionId = FindOption(question, optionText).Id }]);

    /// <summary>Selects the combined "Other" option with its free text.</summary>
    public static AnswerInputDto Other(QuestionDto question, string freeText) =>
        Selections(question, [OtherSelection(question, freeText)]);

    /// <summary>Selects several options, optionally including the "Other" option with free text.</summary>
    public static AnswerInputDto Options(QuestionDto question, IEnumerable<string> optionTexts, string? otherText)
    {
        var selections = optionTexts
            .Select(text => new SelectionInputDto { OptionId = FindOption(question, text).Id })
            .ToList();
        if (otherText is not null)
        {
            selections.Add(OtherSelection(question, otherText));
        }

        return Selections(question, selections);
    }

    /// <summary>A text answer.</summary>
    public static AnswerInputDto Text(QuestionDto question, string text) => new() { QuestionId = question.Id, Text = text };

    /// <summary>A text answer, or null when <paramref name="text"/> is null.</summary>
    public static AnswerInputDto? TextOrNull(QuestionDto question, string? text) => text is null ? null : Text(question, text);

    /// <summary>A numeric answer (number, rating, scale).</summary>
    public static AnswerInputDto Number(QuestionDto question, double value) => new() { QuestionId = question.Id, Number = value };

    /// <summary>A date answer.</summary>
    public static AnswerInputDto Date(QuestionDto question, DateOnly value) => new() { QuestionId = question.Id, Date = value };

    private static AnswerInputDto Selections(QuestionDto question, List<SelectionInputDto> selections) =>
        new() { QuestionId = question.Id, Selections = selections };

    private static SelectionInputDto OtherSelection(QuestionDto question, string freeText) => new()
    {
        OptionId = (question.Options.FirstOrDefault(o => o.AllowsFreeText)
            ?? throw new InvalidOperationException($"Question {question.Code} has no free-text option.")).Id,
        FreeText = freeText,
    };

    private static OptionDto FindOption(QuestionDto question, string text) =>
        question.Options.FirstOrDefault(o => o.Text == text)
        ?? throw new InvalidOperationException($"Question {question.Code} has no option '{text}'.");
}

/// <summary>Random helpers for the demo data generator.</summary>
internal static class DemoRandomExtensions
{
    /// <summary>True with probability <paramref name="probability"/> (0..1).</summary>
    public static bool Chance(this Random random, double probability) => random.NextDouble() < probability;

    /// <summary>A uniformly chosen item.</summary>
    public static T Pick<T>(this Random random, IReadOnlyList<T> items) => items[random.Next(items.Count)];

    /// <summary>An index chosen with probability proportional to its weight.</summary>
    public static int PickWeighted(this Random random, IReadOnlyList<int> weights)
    {
        var roll = random.Next(weights.Sum());
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
            {
                return i;
            }
        }

        return weights.Count - 1;
    }

    /// <summary><paramref name="count"/> distinct items, kept in their original order.</summary>
    public static List<T> PickDistinct<T>(this Random random, IReadOnlyList<T> items, int count)
    {
        var chosen = Enumerable.Range(0, items.Count)
            .OrderBy(_ => random.Next())
            .Take(Math.Clamp(count, 0, items.Count))
            .ToHashSet();
        return items.Where((_, index) => chosen.Contains(index)).ToList();
    }
}
