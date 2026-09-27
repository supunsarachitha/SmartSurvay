using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary><see cref="SurveyService.UpdateAsync"/>: reconciliation by id, concurrency, slugs, rules and audit.</summary>
public class SurveyServiceUpdateTests
{
    [Fact]
    public async Task Update_changes_settings_and_increments_the_version()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        h.Db.Time.Advance(TimeSpan.FromMinutes(5));

        saved.Title = "Renamed";
        saved.AllowAnonymous = false;
        saved.AllowMultipleResponses = true;
        saved.IsTemplate = true;
        saved.MaxResponses = 50;
        saved.WelcomeMessage = "Welcome!";
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        Assert.Equal("Renamed", updated.Title);
        Assert.False(updated.AllowAnonymous);
        Assert.True(updated.AllowMultipleResponses);
        Assert.True(updated.IsTemplate);
        Assert.Equal(50, updated.MaxResponses);
        Assert.Equal("Welcome!", updated.WelcomeMessage);
        Assert.Equal(2, updated.Version);
        Assert.Equal(h.Db.UtcNow, updated.UpdatedAt);
        Assert.Equal(saved.CreatedAt, updated.CreatedAt);
        Assert.Equal("customer-feedback", updated.Slug);
    }

    [Fact]
    public async Task Update_ignores_lifecycle_fields_of_the_payload()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        saved.Status = SurveyStatus.Published;
        saved.PublishedAt = h.Db.UtcNow;
        saved.CreatedAt = DateTime.UnixEpoch;
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        Assert.Equal(SurveyStatus.Draft, updated.Status);
        Assert.Null(updated.PublishedAt);
        Assert.Equal(h.Db.UtcNow, updated.CreatedAt);
    }

    [Fact]
    public async Task Update_adds_new_questions_options_and_pages()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        saved.Question("Q1").Options.Add(new OptionDto { Text = "Not sure", Order = 2 });
        saved.Sections.Add(new SectionDto
        {
            Title = "Extra", Order = 2,
            Questions = [new QuestionDto { Type = QuestionType.Dropdown, Text = "Colour", Options = [new OptionDto { Text = "Red" }, new OptionDto { Text = "Blue", Order = 1 }] }],
        });
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        Assert.Equal(["Yes", "No", "Not sure"], updated.Question("Q1").Options.Select(o => o.Text));
        Assert.Equal(3, updated.Sections.Count);
        var colour = updated.Question("Q8");
        Assert.Equal("Colour", colour.Text);
        Assert.Equal(["Red", "Blue"], colour.Options.Select(o => o.Text));
    }

    [Fact]
    public async Task Update_changes_existing_items_in_place()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();

        var age = saved.Question("Q6");
        age.Text = "How old are you?";
        age.IsRequired = true;
        age.Settings.MaxValue = 99;
        saved.Question("Q1").Options[0].Text = "Yes, a lot";
        saved.Sections[1].Title = "Profile";
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        var updatedAge = updated.Question("Q6");
        Assert.Equal(sample.Age.Id, updatedAge.Id);
        Assert.Equal("How old are you?", updatedAge.Text);
        Assert.True(updatedAge.IsRequired);
        Assert.Equal(99, updatedAge.Settings.MaxValue);
        Assert.Equal("Yes, a lot", updated.Question("Q1").Options[0].Text);
        Assert.Equal(sample.Yes.Id, updated.Question("Q1").Options[0].Id);
        Assert.Equal("Profile", updated.Sections[1].Title);
    }

    [Fact]
    public async Task Update_reorders_questions_and_options()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        var page2 = saved.Sections[1];
        page2.Questions.Reverse();
        for (var i = 0; i < page2.Questions.Count; i++)
        {
            page2.Questions[i].Order = i;
        }

        var region = saved.Question("Q7");
        region.Options[0].Order = 5; // Europe last
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        Assert.Equal(["Q7", "Q6", "Q5", "Q4"], updated.Sections[1].Questions.Select(q => q.Code));
        Assert.Equal(["Asia", "Other", "Europe"], updated.Question("Q7").Options.Select(o => o.Text));
    }

    [Fact]
    public async Task Update_moves_a_question_to_another_page()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();

        var email = saved.Question("Q5");
        saved.Sections[1].Questions.Remove(email);
        email.Order = 99;
        saved.Sections[0].Questions.Add(email);
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        Assert.Equal(["Q1", "Q2", "Q3", "Q5"], updated.Sections[0].Questions.Select(q => q.Code));
        Assert.Equal(["Q4", "Q6", "Q7"], updated.Sections[1].Questions.Select(q => q.Code));
        Assert.Equal(sample.Email.Id, updated.Question("Q5").Id);
    }

    [Fact]
    public async Task Update_keeps_questions_moved_away_from_a_deleted_page_and_their_answers()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();
        await h.SeedResponseAsync(saved.Id, ResponseStatus.Completed, h.Db.UtcNow, SurveyServiceHarness.Number(sample.Rating.Id, 4));

        // Drag every question of page 2 onto page 1, then delete page 2.
        var moved = saved.Sections[1].Questions;
        for (var i = 0; i < moved.Count; i++)
        {
            moved[i].Order = 10 + i;
        }

        saved.Sections[0].Questions.AddRange(moved);
        saved.Sections.RemoveAt(1);
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        var page = Assert.Single(updated.Sections);
        Assert.Equal(["Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "Q7"], page.Questions.Select(q => q.Code));
        Assert.Equal(1, await h.Service.CountAnswersAsync(sample.Rating.Id));
    }

    [Fact]
    public async Task Update_moves_an_option_to_another_question()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();

        var region = saved.Question("Q7");
        var other = region.Options.Single(o => o.Text == "Other");
        region.Options.Remove(other);
        saved.Question("Q2").Options.Add(new OptionDto { Id = other.Id, Text = "Something else", Order = 10 });
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        Assert.Equal(["Europe", "Asia"], updated.Question("Q7").Options.Select(o => o.Text));
        var movedOption = updated.Question("Q2").Options.Last();
        Assert.Equal(sample.OtherRegion.Id, movedOption.Id);
        Assert.Equal("Something else", movedOption.Text);
    }

    [Fact]
    public async Task Items_moved_out_of_a_deleted_parent_survive()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();
        var emailRule = new LogicRuleDto
        {
            TargetQuestionId = sample.Email.Id,
            Conditions = [new LogicConditionDto { SourceQuestionId = sample.Rating.Id, Operator = ConditionOperator.LessThan, Value = "3" }],
        };
        saved.LogicRules.Add(emailRule);
        saved = await h.Service.UpdateAsync(saved.Id, saved);
        var movedCondition = saved.LogicRules.Single(r => r.TargetQuestionId == sample.Email.Id).Conditions[0];

        // Move "Other" from Q7 to Q2 and delete Q7; move the e-mail rule's condition to the Q3 rule and delete the e-mail rule.
        var region = saved.Question("Q7");
        saved.Sections[1].Questions.Remove(region);
        saved.Question("Q2").Options.Add(new OptionDto { Id = sample.OtherRegion.Id, Text = "Other region", Order = 9 });
        saved.LogicRules.RemoveAll(r => r.TargetQuestionId == sample.Email.Id);
        var whyNotRule = saved.LogicRules.Single();
        whyNotRule.Conditions.Add(new LogicConditionDto
        {
            Id = movedCondition.Id, SourceQuestionId = sample.Enjoy.Id, Operator = ConditionOperator.IsAnswered,
        });
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        Assert.DoesNotContain(updated.AllQuestions(), q => q.Code == "Q7");
        Assert.Equal(sample.OtherRegion.Id, updated.Question("Q2").Options.Last().Id);
        var rule = Assert.Single(updated.LogicRules);
        Assert.Contains(rule.Conditions, c => c.Id == movedCondition.Id && c.Operator == ConditionOperator.IsAnswered);
    }

    [Fact]
    public async Task Removing_a_question_deletes_its_answers_rules_and_nothing_else()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();
        await h.SeedResponseAsync(saved.Id, ResponseStatus.Completed, h.Db.UtcNow,
            SurveyServiceHarness.Choice(sample.Enjoy.Id, sample.No.Id),
            SurveyServiceHarness.Text(sample.WhyNot.Id, "Too slow"),
            SurveyServiceHarness.Number(sample.Rating.Id, 2));
        Assert.Equal(1, await h.Service.CountAnswersAsync(sample.WhyNot.Id));

        saved.Sections[0].Questions.RemoveAll(q => q.Code == "Q3");
        saved.LogicRules.Clear(); // the rule targeted Q3
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        Assert.DoesNotContain(updated.AllQuestions(), q => q.Code == "Q3");
        Assert.Empty(updated.LogicRules);
        Assert.Equal(0, await h.Service.CountAnswersAsync(sample.WhyNot.Id));
        Assert.Equal(1, await h.Service.CountAnswersAsync(sample.Enjoy.Id));
        Assert.Equal(1, await h.Service.CountAnswersAsync(sample.Rating.Id));
        await using var ctx = h.Db.CreateContext();
        Assert.Equal(1, await ctx.Responses.CountAsync());
    }

    [Fact]
    public async Task Removing_an_option_deletes_the_selections_of_that_option()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();
        await h.SeedResponseAsync(saved.Id, ResponseStatus.Completed, h.Db.UtcNow,
            SurveyServiceHarness.Choice(sample.Features.Id, sample.Reports.Id, sample.Logic.Id));

        saved.Question("Q2").Options.RemoveAll(o => o.Id == sample.Logic.Id);
        await h.Service.UpdateAsync(saved.Id, saved);

        await using var ctx = h.Db.CreateContext();
        var remaining = await ctx.AnswerSelections.Select(s => s.OptionId).ToListAsync();
        Assert.Equal([sample.Reports.Id], remaining);
    }

    [Fact]
    public async Task Update_adds_changes_and_removes_logic_rules()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();

        // Change the existing rule: show Q3 when Q1 = Yes (was No) OR Q2 includes "Reports".
        var rule = saved.LogicRules[0];
        rule.MatchType = LogicMatchType.Any;
        rule.Conditions[0].OptionId = sample.Yes.Id;
        rule.Conditions.Add(new LogicConditionDto { SourceQuestionId = sample.Features.Id, Operator = ConditionOperator.Contains, OptionId = sample.Reports.Id });

        // Add a page rule: hide page 2 when Q1 = No.
        saved.LogicRules.Add(new LogicRuleDto
        {
            TargetSectionId = sample.Page2.Id,
            Action = LogicAction.Hide,
            Conditions = [new LogicConditionDto { SourceQuestionId = sample.Enjoy.Id, Operator = ConditionOperator.Equals, OptionId = sample.No.Id }],
        });
        var updated = await h.Service.UpdateAsync(saved.Id, saved);

        Assert.Equal(2, updated.LogicRules.Count);
        var changed = updated.LogicRules.Single(r => r.Id == rule.Id);
        Assert.Equal(LogicMatchType.Any, changed.MatchType);
        Assert.Equal(2, changed.Conditions.Count);
        Assert.Contains(changed.Conditions, c => c.OptionId == sample.Yes.Id);
        var pageRule = updated.LogicRules.Single(r => r.TargetSectionId == sample.Page2.Id);
        Assert.Equal(LogicAction.Hide, pageRule.Action);

        // Remove the first condition and the page rule.
        changed.Conditions.RemoveAll(c => c.SourceQuestionId == sample.Enjoy.Id);
        updated.LogicRules.Remove(pageRule);
        var final = await h.Service.UpdateAsync(updated.Id, updated);

        var only = Assert.Single(final.LogicRules);
        Assert.Equal(sample.Features.Id, Assert.Single(only.Conditions).SourceQuestionId);
        await using var ctx = h.Db.CreateContext();
        Assert.Equal(1, await ctx.LogicRules.CountAsync());
        Assert.Equal(1, await ctx.LogicConditions.CountAsync());
    }

    [Fact]
    public async Task Logic_rules_are_returned_in_the_display_order_of_their_targets()
    {
        await using var h = new SurveyServiceHarness();
        var sample = BuildSurveyWithRulesOnSeveralTargets(out var expectedTargets);

        var saved = await h.Service.CreateAsync(sample);

        Assert.Equal(expectedTargets, saved.LogicRules.Select(r => r.TargetQuestionId ?? r.TargetSectionId!.Value));
    }

    [Fact]
    public async Task Stale_version_is_a_conflict_and_changes_nothing()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        var otherEditor = await h.Service.GetAsync(saved.Id);

        saved.Title = "First save wins";
        await h.Service.UpdateAsync(saved.Id, saved);

        otherEditor.Title = "Second save loses";
        var ex = await Assert.ThrowsAsync<ConflictException>(() => h.Service.UpdateAsync(otherEditor.Id, otherEditor));

        Assert.Contains("modified by someone else", ex.Message);
        var current = await h.Service.GetAsync(saved.Id);
        Assert.Equal("First save wins", current.Title);
        Assert.Equal(2, current.Version);
    }

    [Fact]
    public async Task Update_of_a_missing_survey_is_not_found()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.UpdateAsync(Guid.NewGuid(), saved));
    }

    [Fact]
    public async Task Archived_surveys_are_read_only()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        await h.Service.ChangeStatusAsync(saved.Id, SurveyStatus.Archived);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.UpdateAsync(saved.Id, saved));

        Assert.Contains("Restore the survey to draft", ex.Message);
    }

    [Fact]
    public async Task Invalid_update_is_rejected_and_the_stored_design_is_unchanged()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();

        saved.Title = "Changed";
        saved.Question("Q3").Text = "";
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.UpdateAsync(saved.Id, saved));

        Assert.Contains("Sections[0].Questions[2].Text", ex.Errors.Keys);
        var current = await h.Service.GetAsync(saved.Id);
        Assert.Equal("Customer feedback", current.Title);
        Assert.Equal(sample.WhyNot.Text, current.Question("Q3").Text);
        Assert.Equal(1, current.Version);
    }

    [Fact]
    public async Task Removing_a_rule_target_without_removing_the_rule_is_a_validation_error()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        saved.Sections[0].Questions.RemoveAll(q => q.Code == "Q3");
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.UpdateAsync(saved.Id, saved));

        Assert.Contains("LogicRules[0].TargetQuestionId", ex.Errors.Keys);
    }

    [Fact]
    public async Task Slug_can_change_to_a_free_value_and_empty_keeps_the_current_one()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        saved.Slug = "New-Link";
        var renamed = await h.Service.UpdateAsync(saved.Id, saved);
        Assert.Equal("new-link", renamed.Slug);

        renamed.Slug = "";
        var kept = await h.Service.UpdateAsync(renamed.Id, renamed);
        Assert.Equal("new-link", kept.Slug);

        kept.Title = "Same slug again";
        var same = await h.Service.UpdateAsync(kept.Id, kept); // its own slug is not a conflict
        Assert.Equal("new-link", same.Slug);
    }

    [Fact]
    public async Task Slug_used_by_another_survey_is_a_conflict()
    {
        await using var h = new SurveyServiceHarness();
        await h.Service.CreateAsync(new SurveyDefinitionDto { Title = "Other", Slug = "taken" });
        var (_, saved) = await h.CreateSampleAsync();

        saved.Slug = "taken";
        var ex = await Assert.ThrowsAsync<ConflictException>(() => h.Service.UpdateAsync(saved.Id, saved));

        Assert.Contains("'taken' is already used", ex.Message);
    }

    [Fact]
    public async Task Update_is_audited()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        await h.Service.UpdateAsync(saved.Id, saved);

        var entry = h.Audit.Entries.Last();
        Assert.Equal(AuditActions.SurveyUpdated, entry.Action);
        Assert.Equal(saved.Id.ToString(), entry.EntityId);
        Assert.Equal("Updated survey 'Customer feedback' (version 2).", entry.Details);
    }

    /// <summary>Sample survey with rules declared in reverse display order of their targets.</summary>
    private static SurveyDefinitionDto BuildSurveyWithRulesOnSeveralTargets(out Guid[] expectedTargetOrder)
    {
        var s = SampleSurveys.CustomerFeedback();
        var emailRule = new LogicRuleDto
        {
            TargetQuestionId = s.Email.Id,
            Conditions = [new LogicConditionDto { SourceQuestionId = s.Rating.Id, Operator = ConditionOperator.LessThan, Value = "3" }],
        };
        var pageRule = new LogicRuleDto
        {
            TargetSectionId = s.Page2.Id,
            Conditions = [new LogicConditionDto { SourceQuestionId = s.Enjoy.Id, Operator = ConditionOperator.IsAnswered }],
        };
        s.Definition.LogicRules = [emailRule, pageRule, s.WhyNotRule];

        expectedTargetOrder = [s.WhyNot.Id, s.Page2.Id, s.Email.Id];
        return s.Definition;
    }
}
