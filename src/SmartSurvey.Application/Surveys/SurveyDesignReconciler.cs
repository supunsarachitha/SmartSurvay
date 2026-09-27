using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Application.Surveys;

/// <summary>
/// Applies an edited design to a tracked survey graph, matching items by id: existing items are
/// updated in place (including questions moved to another page, options moved to another question
/// and conditions moved to another rule), new items are inserted and items missing from the design
/// are deleted. Deleting a question lets the database cascade remove its answers.
/// </summary>
/// <remarks>
/// Moves are expressed only through foreign-key values and every new item is added through its
/// <see cref="DbSet{TEntity}"/>, so EF Core orders the SQL statements by the key values (inserts of
/// new parents before re-parented children, updates of moved children before the deletion of their
/// old parent). Deletions run last: EF Core cascades a deletion only to tracked dependents whose
/// <em>current</em> foreign key still references the deleted item, so children that were moved
/// away (say questions dragged to another page before their old page was deleted) survive.
/// </remarks>
internal static class SurveyDesignReconciler
{
    /// <summary>Reconciles <paramref name="survey"/> (tracked, fully loaded) with <paramref name="design"/> (normalised).</summary>
    /// <param name="db">Context tracking <paramref name="survey"/>.</param>
    /// <param name="survey">Survey with sections, questions + options and rules + conditions loaded.</param>
    /// <param name="design">Validated, normalised design.</param>
    public static void Apply(IAppDbContext db, Survey survey, SurveyDefinitionDto design)
    {
        SurveyEntityMapper.ApplySurveyFields(design, survey);

        var sections = survey.Sections.ToDictionary(s => s.Id);
        var questions = survey.Questions.ToDictionary(q => q.Id);
        var options = survey.Questions.SelectMany(q => q.Options).ToDictionary(o => o.Id);
        var rules = survey.LogicRules.ToDictionary(r => r.Id);
        var conditions = survey.LogicRules.SelectMany(r => r.Conditions).ToDictionary(c => c.Id);

        UpsertSections(db, survey.Id, design, sections, questions, options);
        UpsertRules(db, survey.Id, design, rules, conditions);

        // Leaf-first removal keeps EF's cascade work minimal and predictable (see remarks).
        RemoveMissing(db.LogicConditions, conditions, design.LogicRules.SelectMany(r => r.Conditions).Select(c => c.Id));
        RemoveMissing(db.LogicRules, rules, design.LogicRules.Select(r => r.Id));
        RemoveMissing(db.QuestionOptions, options, design.AllQuestions().SelectMany(q => q.Options).Select(o => o.Id));
        RemoveMissing(db.Questions, questions, design.AllQuestions().Select(q => q.Id));
        RemoveMissing(db.SurveySections, sections, design.Sections.Select(s => s.Id));
    }

    private static void UpsertSections(
        IAppDbContext db,
        Guid surveyId,
        SurveyDefinitionDto design,
        Dictionary<Guid, SurveySection> sections,
        Dictionary<Guid, Question> questions,
        Dictionary<Guid, QuestionOption> options)
    {
        foreach (var sectionDto in design.Sections)
        {
            Upsert(db.SurveySections, sections, sectionDto.Id, () => new SurveySection { Id = sectionDto.Id, SurveyId = surveyId },
                section => SurveyEntityMapper.ApplySection(sectionDto, section));

            foreach (var questionDto in sectionDto.Questions)
            {
                Upsert(db.Questions, questions, questionDto.Id, () => new Question { Id = questionDto.Id, SurveyId = surveyId },
                    question => SurveyEntityMapper.ApplyQuestion(questionDto, question, sectionDto.Id));

                foreach (var optionDto in questionDto.Options)
                {
                    Upsert(db.QuestionOptions, options, optionDto.Id, () => new QuestionOption { Id = optionDto.Id },
                        option => SurveyEntityMapper.ApplyOption(optionDto, option, questionDto.Id));
                }
            }
        }
    }

    private static void UpsertRules(
        IAppDbContext db,
        Guid surveyId,
        SurveyDefinitionDto design,
        Dictionary<Guid, LogicRule> rules,
        Dictionary<Guid, LogicCondition> conditions)
    {
        foreach (var ruleDto in design.LogicRules)
        {
            Upsert(db.LogicRules, rules, ruleDto.Id, () => new LogicRule { Id = ruleDto.Id, SurveyId = surveyId },
                rule => SurveyEntityMapper.ApplyRule(ruleDto, rule));

            foreach (var conditionDto in ruleDto.Conditions)
            {
                Upsert(db.LogicConditions, conditions, conditionDto.Id, () => new LogicCondition { Id = conditionDto.Id },
                    condition => SurveyEntityMapper.ApplyCondition(conditionDto, condition, ruleDto.Id));
            }
        }
    }

    /// <summary>Updates the tracked entity with the item's id, or creates and adds a new one.</summary>
    private static void Upsert<TEntity>(
        DbSet<TEntity> set, Dictionary<Guid, TEntity> existing, Guid id, Func<TEntity> create, Action<TEntity> apply)
        where TEntity : class
    {
        if (existing.TryGetValue(id, out var entity))
        {
            apply(entity);
            return;
        }

        var created = create();
        apply(created);
        set.Add(created);
    }

    /// <summary>Deletes tracked entities whose ids no longer occur in the design.</summary>
    private static void RemoveMissing<TEntity>(DbSet<TEntity> set, Dictionary<Guid, TEntity> existing, IEnumerable<Guid> keptIds)
        where TEntity : class
    {
        var kept = keptIds.ToHashSet();
        foreach (var (id, entity) in existing)
        {
            if (!kept.Contains(id))
            {
                set.Remove(entity);
            }
        }
    }
}
