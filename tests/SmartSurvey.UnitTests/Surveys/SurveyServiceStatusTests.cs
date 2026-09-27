using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary><see cref="SurveyService.ChangeStatusAsync"/> and the <see cref="SurveyStatusTransitions"/> state machine.</summary>
public class SurveyServiceStatusTests
{
    [Theory]
    [InlineData(SurveyStatus.Draft, SurveyStatus.Published)]
    [InlineData(SurveyStatus.Draft, SurveyStatus.Archived)]
    [InlineData(SurveyStatus.Published, SurveyStatus.Closed)]
    [InlineData(SurveyStatus.Published, SurveyStatus.Archived)]
    [InlineData(SurveyStatus.Closed, SurveyStatus.Published)]
    [InlineData(SurveyStatus.Closed, SurveyStatus.Archived)]
    [InlineData(SurveyStatus.Archived, SurveyStatus.Draft)]
    public async Task Allowed_transitions_succeed(SurveyStatus from, SurveyStatus to)
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        await h.SetStatusAsync(saved.Id, from);

        var result = await h.Service.ChangeStatusAsync(saved.Id, to);

        Assert.Equal(to, result.Status);
        Assert.Equal(to, (await h.Service.GetAsync(saved.Id)).Status);
    }

    [Theory]
    [InlineData(SurveyStatus.Draft, SurveyStatus.Closed, "A draft survey can only be published or archived.")]
    [InlineData(SurveyStatus.Published, SurveyStatus.Draft, "A published survey can only be closed or archived.")]
    [InlineData(SurveyStatus.Closed, SurveyStatus.Draft, "A closed survey can only be reopened (published again) or archived.")]
    [InlineData(SurveyStatus.Archived, SurveyStatus.Published, "An archived survey can only be restored to draft.")]
    [InlineData(SurveyStatus.Archived, SurveyStatus.Closed, "An archived survey can only be restored to draft.")]
    public async Task Other_transitions_are_rejected_with_guidance(SurveyStatus from, SurveyStatus to, string message)
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        await h.SetStatusAsync(saved.Id, from);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.ChangeStatusAsync(saved.Id, to));

        Assert.Equal(message, ex.Message);
        Assert.Equal(from, (await h.Service.GetAsync(saved.Id)).Status);
    }

    [Fact]
    public async Task Changing_to_the_current_status_is_a_no_op()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        h.Audit.Entries.Clear();

        var result = await h.Service.ChangeStatusAsync(saved.Id, SurveyStatus.Draft);

        Assert.Equal(SurveyStatus.Draft, result.Status);
        Assert.Null(result.UpdatedAt);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Publishing_requires_at_least_one_question()
    {
        await using var h = new SurveyServiceHarness();
        var empty = await h.Service.CreateAsync(new SurveyDefinitionDto { Title = "No questions yet" });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.ChangeStatusAsync(empty.Id, SurveyStatus.Published));

        Assert.Equal("Add at least one question before publishing the survey.", ex.Message);
    }

    [Fact]
    public async Task Templates_cannot_be_published()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Definition.IsTemplate = true;
        var template = await h.Service.CreateAsync(sample.Definition);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.ChangeStatusAsync(template.Id, SurveyStatus.Published));

        Assert.Contains("Templates cannot be published", ex.Message);
    }

    [Fact]
    public async Task Publish_close_and_reopen_stamp_the_lifecycle_timestamps()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        h.Db.Time.Advance(TimeSpan.FromHours(1));
        var published = await h.Service.ChangeStatusAsync(saved.Id, SurveyStatus.Published);
        var publishedAt = h.Db.UtcNow;
        Assert.Equal(publishedAt, published.PublishedAt);

        h.Db.Time.Advance(TimeSpan.FromDays(3));
        await h.Service.ChangeStatusAsync(saved.Id, SurveyStatus.Closed);
        var closed = await h.LoadSurveyAsync(saved.Id);
        Assert.Equal(h.Db.UtcNow, closed.ClosedAt);
        Assert.Equal(publishedAt, closed.PublishedAt);

        h.Db.Time.Advance(TimeSpan.FromDays(1));
        var reopened = await h.Service.ChangeStatusAsync(saved.Id, SurveyStatus.Published);
        Assert.Equal(h.Db.UtcNow, reopened.PublishedAt);
        Assert.Null((await h.LoadSurveyAsync(saved.Id)).ClosedAt);
    }

    [Fact]
    public async Task Status_changes_do_not_bump_the_design_version()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        await h.Service.ChangeStatusAsync(saved.Id, SurveyStatus.Published);

        // The builder still holds version 1 and can save without a conflict.
        saved.Title = "Edited while live";
        var updated = await h.Service.UpdateAsync(saved.Id, saved);
        Assert.Equal(SurveyStatus.Published, updated.Status);
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public async Task Status_change_is_audited_with_from_and_to()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        await h.Service.ChangeStatusAsync(saved.Id, SurveyStatus.Published);

        var entry = h.Audit.Entries.Last();
        Assert.Equal(AuditActions.SurveyStatusChanged, entry.Action);
        Assert.Equal(saved.Id.ToString(), entry.EntityId);
        Assert.Equal("Draft → Published", entry.Details);
    }

    [Fact]
    public async Task Unknown_status_value_is_a_validation_error()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.ChangeStatusAsync(saved.Id, (SurveyStatus)42));

        Assert.Contains("Status", ex.Errors.Keys);
    }

    [Fact]
    public async Task Changing_the_status_of_a_missing_survey_is_not_found()
    {
        await using var h = new SurveyServiceHarness();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.ChangeStatusAsync(Guid.NewGuid(), SurveyStatus.Published));
    }

    [Theory]
    [InlineData(SurveyStatus.Draft, new[] { SurveyStatus.Published, SurveyStatus.Archived })]
    [InlineData(SurveyStatus.Published, new[] { SurveyStatus.Closed, SurveyStatus.Archived })]
    [InlineData(SurveyStatus.Closed, new[] { SurveyStatus.Published, SurveyStatus.Archived })]
    [InlineData(SurveyStatus.Archived, new[] { SurveyStatus.Draft })]
    public void State_machine_lists_the_reachable_states(SurveyStatus from, SurveyStatus[] expected)
    {
        Assert.Equal(expected, SurveyStatusTransitions.AllowedTargets(from));
        Assert.All(expected, to => Assert.True(SurveyStatusTransitions.IsAllowed(from, to)));
        Assert.False(SurveyStatusTransitions.IsAllowed(from, from));
    }
}
