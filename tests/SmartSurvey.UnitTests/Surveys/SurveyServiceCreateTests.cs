using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary><see cref="SurveyService.CreateAsync"/>: persistence, normalisation, slugs, validation and audit.</summary>
public class SurveyServiceCreateTests
{
    [Fact]
    public async Task Create_persists_the_design_as_a_version_1_draft()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Definition.Status = SurveyStatus.Published; // ignored: new surveys are always drafts
        sample.Definition.Version = 42;

        var saved = await h.Service.CreateAsync(sample.Definition);

        Assert.Equal(SurveyStatus.Draft, saved.Status);
        Assert.Equal(1, saved.Version);
        Assert.Equal(h.Db.UtcNow, saved.CreatedAt);
        Assert.Null(saved.PublishedAt);
        Assert.Equal("Customer feedback", saved.Title);
        Assert.Equal(["Experience", "About you"], saved.Sections.Select(s => s.Title));
        Assert.Equal(["Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "Q7"], saved.AllQuestions().Select(q => q.Code));

        // Client-generated ids are kept, so logic references stay intact.
        Assert.Equal(sample.Enjoy.Id, saved.Question("Q1").Id);
        var rule = Assert.Single(saved.LogicRules);
        Assert.Equal(sample.WhyNot.Id, rule.TargetQuestionId);
        Assert.Equal(sample.No.Id, Assert.Single(rule.Conditions).OptionId);
        Assert.Equal("Please specify", saved.Question("Q2").Options[2].FreeTextPlaceholder);
        Assert.Equal(120, saved.Question("Q6").Settings.MaxValue);
    }

    [Fact]
    public async Task Create_does_not_modify_the_callers_object()
    {
        await using var h = new SurveyServiceHarness();
        var dto = new SurveyDefinitionDto
        {
            Title = "  Padded  ",
            Sections = [new SectionDto { Id = Guid.Empty, Title = "P", Order = 7, Questions = [new QuestionDto { Id = Guid.Empty, Text = "Q" }] }],
        };

        await h.Service.CreateAsync(dto);

        Assert.Equal("  Padded  ", dto.Title);
        Assert.Equal(Guid.Empty, dto.Sections[0].Id);
        Assert.Equal(Guid.Empty, dto.Sections[0].Questions[0].Id);
        Assert.Null(dto.Sections[0].Questions[0].Code);
        Assert.Equal(7, dto.Sections[0].Order);
    }

    [Fact]
    public async Task Create_assigns_ids_to_items_without_one()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Definition.Id = Guid.Empty;
        sample.Page2.Id = Guid.Empty;
        sample.Email.Id = Guid.Empty;
        sample.Europe.Id = Guid.Empty;
        sample.WhyNotRule.Id = Guid.Empty;
        sample.WhyNotRule.Conditions[0].Id = Guid.Empty;

        var saved = await h.Service.CreateAsync(sample.Definition);

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.DoesNotContain(Guid.Empty, saved.AllItemIds());
        Assert.Equal(sample.WhyNot.Id, saved.LogicRules[0].TargetQuestionId);
    }

    [Fact]
    public async Task Create_adds_a_default_page_when_none_is_given()
    {
        await using var h = new SurveyServiceHarness();

        var saved = await h.Service.CreateAsync(new SurveyDefinitionDto { Title = "Empty" });

        var page = Assert.Single(saved.Sections);
        Assert.Equal("Page 1", page.Title);
        Assert.Equal(0, page.Order);
        Assert.Empty(page.Questions);
    }

    [Fact]
    public async Task Create_renumbers_orders_keeping_the_submitted_sequence()
    {
        await using var h = new SurveyServiceHarness();
        var dto = new SurveyDefinitionDto
        {
            Title = "Orders",
            Sections =
            [
                new SectionDto
                {
                    Title = "Second", Order = 9,
                    Questions =
                    [
                        new QuestionDto { Text = "B", Order = 30 },
                        new QuestionDto { Text = "A", Order = 3 },
                    ],
                },
                new SectionDto
                {
                    Title = "First", Order = 2,
                    Questions =
                    [
                        new QuestionDto
                        {
                            Type = QuestionType.Radio, Text = "Pick", Order = 5,
                            Options = [new OptionDto { Text = "Z", Order = 8 }, new OptionDto { Text = "Y", Order = 1 }],
                        },
                    ],
                },
            ],
        };

        var saved = await h.Service.CreateAsync(dto);

        Assert.Equal(["First", "Second"], saved.Sections.Select(s => s.Title));
        Assert.Equal([0, 1], saved.Sections.Select(s => s.Order));
        Assert.Equal(["A", "B"], saved.Sections[1].Questions.Select(q => q.Text));
        Assert.Equal([0, 1], saved.Sections[1].Questions.Select(q => q.Order));
        Assert.Equal(["Y", "Z"], saved.Sections[0].Questions[0].Options.Select(o => o.Text));
        Assert.Equal([0, 1], saved.Sections[0].Questions[0].Options.Select(o => o.Order));
    }

    [Fact]
    public async Task Create_generates_missing_codes_in_display_order_skipping_used_codes()
    {
        await using var h = new SurveyServiceHarness();
        var dto = new SurveyDefinitionDto
        {
            Title = "Codes",
            Sections =
            [
                new SectionDto
                {
                    Title = "P1",
                    Questions =
                    [
                        new QuestionDto { Text = "first", Order = 0 },
                        new QuestionDto { Text = "second", Order = 1, Code = "q1" },
                        new QuestionDto { Text = "third", Order = 2, Code = "  " },
                    ],
                },
                new SectionDto { Title = "P2", Order = 1, Questions = [new QuestionDto { Text = "fourth", Code = "Custom_code" }, new QuestionDto { Text = "fifth", Order = 1 }] },
            ],
        };

        var saved = await h.Service.CreateAsync(dto);

        // "q1" is taken (case-insensitively), so generation continues with Q2, Q3, Q4.
        Assert.Equal(["Q2", "q1", "Q3", "Custom_code", "Q4"], saved.AllQuestions().Select(q => q.Code));
    }

    [Fact]
    public async Task Create_rejects_duplicate_codes_supplied_by_the_client()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Email.Code = "Q1";

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.CreateAsync(sample.Definition));

        Assert.Contains("Sections[1].Questions[1].Code", ex.Errors.Keys);
        Assert.Contains("already used", ex.Message);
    }

    [Fact]
    public async Task Create_trims_texts_and_drops_options_of_non_choice_questions()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Definition.Title = "  Spaced title  ";
        sample.Definition.Description = "   ";
        sample.Page1.Title = " Experience ";
        sample.Enjoy.Text = "  Did you enjoy it?  ";
        sample.Yes.Text = " Yes ";
        sample.Yes.FreeTextPlaceholder = "ignored: option has no free text";
        sample.Email.Options.Add(new OptionDto { Text = "left over from a type change" });

        var saved = await h.Service.CreateAsync(sample.Definition);

        Assert.Equal("Spaced title", saved.Title);
        Assert.Null(saved.Description);
        Assert.Equal("Experience", saved.Sections[0].Title);
        Assert.Equal("Did you enjoy it?", saved.Question("Q1").Text);
        Assert.Equal("Yes", saved.Question("Q1").Options[0].Text);
        Assert.Null(saved.Question("Q1").Options[0].FreeTextPlaceholder);
        Assert.Empty(saved.Question("Q5").Options);
    }

    [Fact]
    public async Task Create_cleans_logic_condition_operands()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        // Option id sent as the value (accepted by the evaluator) becomes the canonical OptionId.
        sample.WhyNotRule.Conditions[0].OptionId = null;
        sample.WhyNotRule.Conditions[0].Value = sample.No.Id.ToString();
        // Unary operators carry no operand.
        sample.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetQuestionId = sample.Email.Id,
            Conditions = [new LogicConditionDto { SourceQuestionId = sample.Rating.Id, Operator = ConditionOperator.IsAnswered, Value = "junk", OptionId = sample.Yes.Id }],
        });

        var saved = await h.Service.CreateAsync(sample.Definition);

        var choiceCondition = saved.LogicRules.Single(r => r.TargetQuestionId == sample.WhyNot.Id).Conditions[0];
        Assert.Equal(sample.No.Id, choiceCondition.OptionId);
        Assert.Null(choiceCondition.Value);
        var unary = saved.LogicRules.Single(r => r.TargetQuestionId == sample.Email.Id).Conditions[0];
        Assert.Null(unary.OptionId);
        Assert.Null(unary.Value);
    }

    [Fact]
    public async Task Create_generates_a_unique_slug_from_the_title()
    {
        await using var h = new SurveyServiceHarness();

        var first = await h.Service.CreateAsync(new SurveyDefinitionDto { Title = "Café Menu 2026!" });
        var second = await h.Service.CreateAsync(new SurveyDefinitionDto { Title = "Café menu 2026" });
        var third = await h.Service.CreateAsync(new SurveyDefinitionDto { Title = "CAFE MENU 2026" });

        Assert.Equal("cafe-menu-2026", first.Slug);
        Assert.Equal("cafe-menu-2026-2", second.Slug);
        Assert.Equal("cafe-menu-2026-3", third.Slug);
    }

    [Fact]
    public async Task Create_uses_a_supplied_slug_after_trimming_and_lowercasing()
    {
        await using var h = new SurveyServiceHarness();

        var saved = await h.Service.CreateAsync(new SurveyDefinitionDto { Title = "Anything", Slug = "  Team-Pulse  " });

        Assert.Equal("team-pulse", saved.Slug);
    }

    [Fact]
    public async Task Create_with_a_taken_slug_is_a_conflict()
    {
        await using var h = new SurveyServiceHarness();
        await h.Service.CreateAsync(new SurveyDefinitionDto { Title = "One", Slug = "pulse" });

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            h.Service.CreateAsync(new SurveyDefinitionDto { Title = "Two", Slug = "PULSE" }));

        Assert.Contains("'pulse' is already used", ex.Message);
    }

    [Fact]
    public async Task Create_with_a_malformed_slug_is_a_validation_error()
    {
        await using var h = new SurveyServiceHarness();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            h.Service.CreateAsync(new SurveyDefinitionDto { Title = "One", Slug = "no spaces allowed" }));

        Assert.Contains("Slug", ex.Errors.Keys);
    }

    [Fact]
    public async Task Invalid_design_is_rejected_with_all_errors_and_nothing_is_saved()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Definition.Title = "";
        sample.Enjoy.Options.RemoveAt(1);
        sample.WhyNotRule.Conditions[0].OptionId = null;

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.CreateAsync(sample.Definition));

        Assert.Contains("Title", ex.Errors.Keys);
        Assert.Contains("Sections[0].Questions[0].Options", ex.Errors.Keys);
        Assert.Contains("LogicRules[0].Conditions[0].OptionId", ex.Errors.Keys);
        await using var ctx = h.Db.CreateContext();
        Assert.Equal(0, await ctx.Surveys.CountAsync());
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Create_with_the_id_of_an_existing_survey_is_a_conflict()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => h.Service.CreateAsync(saved));

        Assert.Contains("Duplicate or Import", ex.Message);
    }

    [Fact]
    public async Task Create_with_item_ids_that_belong_to_another_survey_is_a_conflict()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        saved.Id = Guid.Empty; // new survey, but pages/questions reuse the ids of the existing one
        saved.Slug = null;

        await Assert.ThrowsAsync<ConflictException>(() => h.Service.CreateAsync(saved));
    }

    [Fact]
    public async Task Create_is_audited()
    {
        await using var h = new SurveyServiceHarness();

        var (_, saved) = await h.CreateSampleAsync();

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActions.SurveyCreated, entry.Action);
        Assert.Equal("Survey", entry.EntityType);
        Assert.Equal(saved.Id.ToString(), entry.EntityId);
        Assert.Equal("Created survey 'Customer feedback' (2 pages, 7 questions).", entry.Details);
    }
}
