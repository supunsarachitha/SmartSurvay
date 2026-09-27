namespace SmartSurvey.Application.Surveys.Validation;

/// <summary>
/// Survey-wide uniqueness rules that a per-item validator cannot see: every item id must be unique
/// across the whole design (sections, questions, options, rules and conditions share one id space
/// because logic rules reference them), and question codes must be unique per survey ignoring case
/// (they become column headers in exports).
/// </summary>
internal static class SurveyUniquenessChecks
{
    /// <summary>Reports duplicate ids and duplicate question codes.</summary>
    /// <param name="survey">Design to check.</param>
    /// <param name="addFailure">Receives (property path, message) for each violation.</param>
    public static void Check(SurveyDefinitionDto survey, Action<string, string> addFailure)
    {
        var ids = new HashSet<Guid>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void CheckId(Guid id, string path)
        {
            // Guid.Empty means "assign an id for me" and is replaced before saving.
            if (id != Guid.Empty && !ids.Add(id))
            {
                addFailure($"{path}.Id", "This item has the same id as another item of the survey. Every page, question, option, rule and condition needs its own id.");
            }
        }

        var sections = survey.Sections.OrEmpty();
        for (var s = 0; s < sections.Count; s++)
        {
            if (sections[s] is null)
            {
                continue;
            }

            var sectionPath = $"Sections[{s}]";
            CheckId(sections[s].Id, sectionPath);

            var questions = sections[s].Questions.OrEmpty();
            for (var q = 0; q < questions.Count; q++)
            {
                var question = questions[q];
                if (question is null)
                {
                    continue;
                }

                var questionPath = $"{sectionPath}.Questions[{q}]";
                CheckId(question.Id, questionPath);

                if (!string.IsNullOrWhiteSpace(question.Code) && !codes.Add(question.Code.Trim()))
                {
                    addFailure($"{questionPath}.Code", $"The code '{question.Code.Trim()}' is already used by another question. Question codes must be unique within a survey.");
                }

                var options = question.Options.OrEmpty();
                for (var o = 0; o < options.Count; o++)
                {
                    if (options[o] is not null)
                    {
                        CheckId(options[o].Id, $"{questionPath}.Options[{o}]");
                    }
                }
            }
        }

        var rules = survey.LogicRules.OrEmpty();
        for (var r = 0; r < rules.Count; r++)
        {
            if (rules[r] is null)
            {
                continue;
            }

            var rulePath = $"LogicRules[{r}]";
            CheckId(rules[r].Id, rulePath);

            var conditions = rules[r].Conditions.OrEmpty();
            for (var c = 0; c < conditions.Count; c++)
            {
                if (conditions[c] is not null)
                {
                    CheckId(conditions[c].Id, $"{rulePath}.Conditions[{c}]");
                }
            }
        }
    }
}

/// <summary>Null-tolerant list access for designs that did not pass through the normaliser.</summary>
internal static class SurveyListExtensions
{
    /// <summary>
    /// Returns the list, or an empty list when it is null (possible for JSON payloads that send
    /// <c>null</c> explicitly). Indices stay aligned with the original list so error paths match.
    /// </summary>
    public static IReadOnlyList<T> OrEmpty<T>(this List<T>? list) => list ?? [];
}
