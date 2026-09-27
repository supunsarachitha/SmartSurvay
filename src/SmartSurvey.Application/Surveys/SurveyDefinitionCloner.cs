using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Surveys;

/// <summary>
/// Deep copies of survey designs. <see cref="Copy"/> keeps every id (a private working copy that
/// can be normalised without touching the caller's object); <see cref="CopyWithNewIds"/> gives every
/// item a brand-new id and rewrites all internal references — logic targets, condition source
/// questions and condition options — so the copy is an independent design (duplicate, template,
/// import).
/// </summary>
/// <remarks>
/// The cloner tolerates null collections and null entries (possible in JSON payloads) by skipping
/// them. References that point outside the design are copied unchanged, so validation still
/// reports them as missing instead of silently repairing them.
/// </remarks>
public static class SurveyDefinitionCloner
{
    /// <summary>Deep copy with identical ids.</summary>
    /// <param name="source">Design to copy.</param>
    public static SurveyDefinitionDto Copy(SurveyDefinitionDto source) => Clone(source, new IdTranslator(generateNewIds: false));

    /// <summary>Deep copy in which every id is new and all references are remapped consistently.</summary>
    /// <param name="source">Design to copy.</param>
    public static SurveyDefinitionDto CopyWithNewIds(SurveyDefinitionDto source) => Clone(source, new IdTranslator(generateNewIds: true));

    private static SurveyDefinitionDto Clone(SurveyDefinitionDto source, IdTranslator ids)
    {
        ArgumentNullException.ThrowIfNull(source);

        var copy = new SurveyDefinitionDto
        {
            Id = ids.Define(source.Id),
            Title = source.Title,
            Description = source.Description,
            Slug = source.Slug,
            Status = source.Status,
            IsTemplate = source.IsTemplate,
            AllowAnonymous = source.AllowAnonymous,
            AllowMultipleResponses = source.AllowMultipleResponses,
            ShowProgressBar = source.ShowProgressBar,
            ShowQuestionNumbers = source.ShowQuestionNumbers,
            OpensAt = source.OpensAt,
            ClosesAt = source.ClosesAt,
            MaxResponses = source.MaxResponses,
            WelcomeMessage = source.WelcomeMessage,
            ThankYouMessage = source.ThankYouMessage,
            Version = source.Version,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            PublishedAt = source.PublishedAt,

            // Pages (and with them questions and options) are copied first so every id that a logic
            // rule can reference is already known to the translator when the rules are copied.
            Sections = NonNull(source.Sections).Select(s => CloneSection(s, ids)).ToList(),
        };

        copy.LogicRules = NonNull(source.LogicRules).Select(r => CloneRule(r, ids)).ToList();
        return copy;
    }

    private static SectionDto CloneSection(SectionDto source, IdTranslator ids) => new()
    {
        Id = ids.Define(source.Id),
        Title = source.Title,
        Description = source.Description,
        Order = source.Order,
        Questions = NonNull(source.Questions).Select(q => CloneQuestion(q, ids)).ToList(),
    };

    private static QuestionDto CloneQuestion(QuestionDto source, IdTranslator ids) => new()
    {
        Id = ids.Define(source.Id),
        Type = source.Type,
        Text = source.Text,
        Description = source.Description,
        Code = source.Code,
        Order = source.Order,
        IsRequired = source.IsRequired,
        Settings = source.Settings?.Clone() ?? new QuestionSettings(),
        Options = NonNull(source.Options).Select(o => CloneOption(o, ids)).ToList(),
    };

    private static OptionDto CloneOption(OptionDto source, IdTranslator ids) => new()
    {
        Id = ids.Define(source.Id),
        Text = source.Text,
        Value = source.Value,
        Order = source.Order,
        AllowsFreeText = source.AllowsFreeText,
        FreeTextPlaceholder = source.FreeTextPlaceholder,
    };

    private static LogicRuleDto CloneRule(LogicRuleDto source, IdTranslator ids) => new()
    {
        Id = ids.Define(source.Id),
        TargetQuestionId = ids.Resolve(source.TargetQuestionId),
        TargetSectionId = ids.Resolve(source.TargetSectionId),
        Action = source.Action,
        MatchType = source.MatchType,
        Conditions = NonNull(source.Conditions).Select(c => new LogicConditionDto
        {
            Id = ids.Define(c.Id),
            SourceQuestionId = ids.Resolve(c.SourceQuestionId),
            Operator = c.Operator,
            OptionId = ids.Resolve(c.OptionId),
            Value = c.Value,
        }).ToList(),
    };

    private static IEnumerable<T> NonNull<T>(List<T>? items) where T : class =>
        items?.Where(item => item is not null) ?? [];

    /// <summary>Maps original ids to the ids used by the copy.</summary>
    private sealed class IdTranslator(bool generateNewIds)
    {
        private readonly Dictionary<Guid, Guid> _map = [];

        /// <summary>
        /// Returns the id of a copied item. A repeated original id maps to the same new id, so a
        /// design with duplicate ids stays invalid (and is reported) instead of being repaired.
        /// </summary>
        public Guid Define(Guid original)
        {
            if (!generateNewIds)
            {
                return original;
            }

            if (original == Guid.Empty)
            {
                return Guid.NewGuid(); // nothing can reference an empty id
            }

            if (!_map.TryGetValue(original, out var mapped))
            {
                mapped = Guid.NewGuid();
                _map[original] = mapped;
            }

            return mapped;
        }

        /// <summary>Translates a reference; ids unknown to the design are kept unchanged.</summary>
        public Guid Resolve(Guid original) => _map.GetValueOrDefault(original, original);

        /// <summary>Translates an optional reference.</summary>
        public Guid? Resolve(Guid? original) => original is { } id ? Resolve(id) : null;
    }
}
