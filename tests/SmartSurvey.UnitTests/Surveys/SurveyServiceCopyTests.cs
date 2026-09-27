using System.Text.Json;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary>Duplicate, export and import of survey designs.</summary>
public class SurveyServiceCopyTests
{
    [Fact]
    public async Task Duplicate_creates_an_identical_design_with_brand_new_ids()
    {
        await using var h = new SurveyServiceHarness();
        var (_, source) = await h.CreateSampleAsync();

        var copy = await h.Service.DuplicateAsync(source.Id, new DuplicateSurveyRequest());

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Empty(source.AllItemIds().Intersect(copy.AllItemIds()));
        Assert.Equal(source.StructureFingerprint(), copy.StructureFingerprint());
    }

    [Fact]
    public async Task Duplicate_remaps_every_logic_reference_to_the_copied_items()
    {
        await using var h = new SurveyServiceHarness();
        var (_, source) = await h.CreateSampleAsync();

        var copy = await h.Service.DuplicateAsync(source.Id, new DuplicateSurveyRequest());

        var rule = Assert.Single(copy.LogicRules);
        var condition = Assert.Single(rule.Conditions);
        Assert.Equal(copy.Question("Q3").Id, rule.TargetQuestionId);
        Assert.Equal(copy.Question("Q1").Id, condition.SourceQuestionId);
        Assert.Equal(copy.Question("Q1").Options.Single(o => o.Text == "No").Id, condition.OptionId);
    }

    [Fact]
    public async Task Duplicate_remaps_page_targets()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetSectionId = sample.Page2.Id,
            Conditions = [new LogicConditionDto { SourceQuestionId = sample.Enjoy.Id, Operator = ConditionOperator.Equals, OptionId = sample.Yes.Id }],
        });
        var source = await h.Service.CreateAsync(sample.Definition);

        var copy = await h.Service.DuplicateAsync(source.Id, new DuplicateSurveyRequest());

        var pageRule = copy.LogicRules.Single(r => r.TargetSectionId is not null);
        Assert.Equal(copy.Sections[1].Id, pageRule.TargetSectionId);
    }

    [Fact]
    public async Task Duplicate_does_not_copy_responses()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, source) = await h.CreateSampleAsync();
        await h.SeedResponseAsync(source.Id, ResponseStatus.Completed, h.Db.UtcNow, SurveyServiceHarness.Choice(sample.Enjoy.Id, sample.Yes.Id));

        var copy = await h.Service.DuplicateAsync(source.Id, new DuplicateSurveyRequest());

        var rows = (await h.Service.ListAsync(new SurveyQuery())).Items;
        Assert.Equal(0, rows.Single(r => r.Id == copy.Id).CompletedResponses);
        Assert.Equal(1, rows.Single(r => r.Id == source.Id).CompletedResponses);
        Assert.Equal(0, await h.Service.CountAnswersAsync(copy.Question("Q1").Id));
    }

    [Fact]
    public async Task Duplicate_defaults_to_a_draft_named_copy_of_with_a_unique_slug()
    {
        await using var h = new SurveyServiceHarness();
        var (_, source) = await h.CreateSampleAsync();
        await h.Service.ChangeStatusAsync(source.Id, SurveyStatus.Published);

        var first = await h.Service.DuplicateAsync(source.Id, new DuplicateSurveyRequest());
        var second = await h.Service.DuplicateAsync(source.Id, new DuplicateSurveyRequest { Title = "   " });

        Assert.Equal("Copy of Customer feedback", first.Title);
        Assert.Equal("copy-of-customer-feedback", first.Slug);
        Assert.Equal("copy-of-customer-feedback-2", second.Slug);
        Assert.Equal(SurveyStatus.Draft, first.Status);
        Assert.Null(first.PublishedAt);
        Assert.Equal(1, first.Version);
        Assert.False(first.IsTemplate);
    }

    [Fact]
    public async Task Duplicate_honours_title_and_template_flag()
    {
        await using var h = new SurveyServiceHarness();
        var (_, source) = await h.CreateSampleAsync();

        var template = await h.Service.DuplicateAsync(source.Id, new DuplicateSurveyRequest { Title = "  NPS template  ", AsTemplate = true });
        var fromTemplate = await h.Service.DuplicateAsync(template.Id, new DuplicateSurveyRequest { Title = "Q3 NPS" });

        Assert.Equal("NPS template", template.Title);
        Assert.True(template.IsTemplate);
        Assert.Equal("nps-template", template.Slug);
        Assert.Equal("Q3 NPS", fromTemplate.Title);
        Assert.False(fromTemplate.IsTemplate);
    }

    [Fact]
    public async Task Duplicate_truncates_the_default_title_to_200_characters()
    {
        await using var h = new SurveyServiceHarness();
        var source = await h.Service.CreateAsync(new SurveyDefinitionDto { Title = new string('t', 200) });

        var copy = await h.Service.DuplicateAsync(source.Id, new DuplicateSurveyRequest());

        Assert.Equal(200, copy.Title.Length);
        Assert.StartsWith("Copy of ttt", copy.Title);
    }

    [Fact]
    public async Task Duplicate_of_a_missing_survey_is_not_found()
    {
        await using var h = new SurveyServiceHarness();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.DuplicateAsync(Guid.NewGuid(), new DuplicateSurveyRequest()));
    }

    [Fact]
    public async Task Duplicate_is_audited_on_the_new_survey()
    {
        await using var h = new SurveyServiceHarness();
        var (_, source) = await h.CreateSampleAsync();

        var copy = await h.Service.DuplicateAsync(source.Id, new DuplicateSurveyRequest());

        var entry = h.Audit.Entries.Last();
        Assert.Equal(AuditActions.SurveyDuplicated, entry.Action);
        Assert.Equal(copy.Id.ToString(), entry.EntityId);
        Assert.Contains($"copy of 'Customer feedback' ({source.Id})", entry.Details);
    }

    [Fact]
    public async Task Export_wraps_the_full_definition_in_a_versioned_document()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        var document = await h.Service.ExportDefinitionAsync(saved.Id);

        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal("SmartSurvey", document.Generator);
        Assert.Equal(h.Db.UtcNow, document.ExportedAt);
        Assert.Equal(saved.Id, document.Survey.Id);
        Assert.Equal(saved.AllItemIds(), document.Survey.AllItemIds());
        Assert.Equal(saved.StructureFingerprint(), document.Survey.StructureFingerprint());

        var entry = h.Audit.Entries.Last();
        Assert.Equal(AuditActions.SurveyExported, entry.Action);
        Assert.Equal(saved.Id.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task Export_of_a_missing_survey_is_not_found()
    {
        await using var h = new SurveyServiceHarness();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.ExportDefinitionAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Import_round_trip_recreates_the_structure_with_new_ids()
    {
        await using var h = new SurveyServiceHarness();
        var (_, original) = await h.CreateSampleAsync();
        var document = await h.Service.ExportDefinitionAsync(original.Id);

        var imported = await h.Service.ImportDefinitionAsync(document);

        Assert.NotEqual(original.Id, imported.Id);
        Assert.Empty(original.AllItemIds().Intersect(imported.AllItemIds()));
        Assert.Equal(original.StructureFingerprint(), imported.StructureFingerprint());
        Assert.Equal(original.Title, imported.Title);
        Assert.Equal("customer-feedback-2", imported.Slug); // the original slug is taken
        var rule = Assert.Single(imported.LogicRules);
        Assert.Equal(imported.Question("Q3").Id, rule.TargetQuestionId);
    }

    [Fact]
    public async Task Import_survives_json_serialisation_of_the_document()
    {
        await using var h = new SurveyServiceHarness();
        var (_, original) = await h.CreateSampleAsync();
        var json = JsonSerializer.Serialize(await h.Service.ExportDefinitionAsync(original.Id));

        var document = JsonSerializer.Deserialize<SurveyExportDocument>(json)!;
        var imported = await h.Service.ImportDefinitionAsync(document);

        Assert.Equal(original.StructureFingerprint(), imported.StructureFingerprint());
    }

    [Fact]
    public async Task Import_keeps_a_free_slug_and_replaces_a_malformed_one()
    {
        await using var h = new SurveyServiceHarness();
        var (_, original) = await h.CreateSampleAsync();
        var document = await h.Service.ExportDefinitionAsync(original.Id);

        document.Survey.Slug = "fresh-link";
        var kept = await h.Service.ImportDefinitionAsync(document);
        document.Survey.Slug = "Not a valid slug!";
        var replaced = await h.Service.ImportDefinitionAsync(document);

        Assert.Equal("fresh-link", kept.Slug);
        Assert.Equal("customer-feedback-2", replaced.Slug);
    }

    [Fact]
    public async Task Import_starts_as_a_version_1_draft_even_for_published_exports()
    {
        await using var h = new SurveyServiceHarness();
        var (_, original) = await h.CreateSampleAsync();
        await h.Service.ChangeStatusAsync(original.Id, SurveyStatus.Published);
        original.Title = "Edited";
        await h.Service.UpdateAsync(original.Id, original);
        var document = await h.Service.ExportDefinitionAsync(original.Id);
        Assert.Equal(SurveyStatus.Published, document.Survey.Status);

        var imported = await h.Service.ImportDefinitionAsync(document);

        Assert.Equal(SurveyStatus.Draft, imported.Status);
        Assert.Equal(1, imported.Version);
        Assert.Null(imported.PublishedAt);
    }

    [Fact]
    public async Task The_same_file_can_be_imported_repeatedly()
    {
        await using var h = new SurveyServiceHarness();
        var (_, original) = await h.CreateSampleAsync();
        var document = await h.Service.ExportDefinitionAsync(original.Id);

        var first = await h.Service.ImportDefinitionAsync(document);
        var second = await h.Service.ImportDefinitionAsync(document);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Empty(first.AllItemIds().Intersect(second.AllItemIds()));
        Assert.Equal("customer-feedback-2", first.Slug);
        Assert.Equal("customer-feedback-3", second.Slug);
        Assert.Equal(original.Id, document.Survey.Id); // the document itself is not modified
    }

    [Fact]
    public async Task Import_of_an_external_file_assigns_ids_and_codes()
    {
        await using var h = new SurveyServiceHarness();
        var document = new SurveyExportDocument
        {
            Survey = new SurveyDefinitionDto
            {
                Id = Guid.Empty,
                Title = "Hand written",
                Sections = [new SectionDto { Id = Guid.Empty, Title = "Only page", Questions = [new QuestionDto { Id = Guid.Empty, Text = "Name?" }] }],
                LogicRules = null!,
            },
        };

        var imported = await h.Service.ImportDefinitionAsync(document);

        Assert.Equal("Q1", imported.Question("Q1").Code);
        Assert.DoesNotContain(Guid.Empty, imported.AllItemIds());
        Assert.Equal("hand-written", imported.Slug);
    }

    [Fact]
    public async Task Import_rejects_documents_without_a_survey()
    {
        await using var h = new SurveyServiceHarness();

        var missingDocument = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.ImportDefinitionAsync(null!));
        var missingSurvey = await Assert.ThrowsAsync<AppValidationException>(() =>
            h.Service.ImportDefinitionAsync(new SurveyExportDocument { Survey = null! }));

        Assert.Contains("Survey", missingDocument.Errors.Keys);
        Assert.Contains("Survey", missingSurvey.Errors.Keys);
    }

    [Fact]
    public async Task Import_rejects_unsupported_schema_versions()
    {
        await using var h = new SurveyServiceHarness();
        var document = new SurveyExportDocument { SchemaVersion = 2, Survey = SampleSurveys.CustomerFeedback().Definition };

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.ImportDefinitionAsync(document));

        Assert.Contains("SchemaVersion", ex.Errors.Keys);
        Assert.Contains("version 2", ex.Message);
    }

    [Fact]
    public async Task Import_validates_the_design()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.WhyNotRule.Conditions[0].SourceQuestionId = Guid.NewGuid(); // dangling reference stays dangling

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            h.Service.ImportDefinitionAsync(new SurveyExportDocument { Survey = sample.Definition }));

        Assert.Contains("LogicRules[0].Conditions[0].SourceQuestionId", ex.Errors.Keys);
    }

    [Fact]
    public async Task Import_is_audited()
    {
        await using var h = new SurveyServiceHarness();

        var imported = await h.Service.ImportDefinitionAsync(new SurveyExportDocument { Survey = SampleSurveys.CustomerFeedback().Definition });

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActions.SurveyImported, entry.Action);
        Assert.Equal(imported.Id.ToString(), entry.EntityId);
        Assert.Equal("Imported survey 'Customer feedback' (2 pages, 7 questions).", entry.Details);
    }
}
