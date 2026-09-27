using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary>Every survey design operation is admin-only, even when called in-process.</summary>
public class SurveyServiceAuthorizationTests
{
    /// <summary>One invocation of every <see cref="ISurveyService"/> member against an existing survey.</summary>
    public static TheoryData<string, Func<ISurveyService, Guid, Task>> Operations => new()
    {
        { "List", (s, _) => s.ListAsync(new SurveyQuery()) },
        { "ListTemplates", (s, _) => s.ListTemplatesAsync() },
        { "Get", (s, id) => s.GetAsync(id) },
        { "FindBySlug", (s, _) => s.FindBySlugAsync("customer-feedback") },
        { "Create", (s, _) => s.CreateAsync(SampleSurveys.CustomerFeedback().Definition) },
        { "Update", (s, id) => s.UpdateAsync(id, SampleSurveys.CustomerFeedback().Definition) },
        { "Delete", (s, id) => s.DeleteAsync(id) },
        { "ChangeStatus", (s, id) => s.ChangeStatusAsync(id, SurveyStatus.Published) },
        { "Duplicate", (s, id) => s.DuplicateAsync(id, new DuplicateSurveyRequest()) },
        { "Export", (s, id) => s.ExportDefinitionAsync(id) },
        { "Import", (s, _) => s.ImportDefinitionAsync(new SurveyExportDocument { Survey = SampleSurveys.CustomerFeedback().Definition }) },
        { "IsSlugAvailable", (s, _) => s.IsSlugAvailableAsync("anything") },
        { "CountAnswers", (s, _) => s.CountAnswersAsync(Guid.NewGuid()) },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Respondents_are_forbidden(string operation, Func<ISurveyService, Guid, Task> call)
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        h.Audit.Entries.Clear();
        h.Db.CurrentUser.ActAsRespondent();

        var ex = await Record.ExceptionAsync(() => call(h.Service, saved.Id));

        AssertForbidden(operation, ex);
        Assert.Equal("Only administrators can manage surveys.", ex!.Message);
        Assert.Empty(h.Audit.Entries);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Anonymous_visitors_are_forbidden(string operation, Func<ISurveyService, Guid, Task> call)
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        h.Db.CurrentUser.ActAsAnonymous();

        AssertForbidden(operation, await Record.ExceptionAsync(() => call(h.Service, saved.Id)));
    }

    private static void AssertForbidden(string operation, Exception? exception) =>
        Assert.True(exception is ForbiddenException, $"{operation} should throw ForbiddenException but threw {exception?.GetType().Name ?? "nothing"}.");

    [Fact]
    public async Task Forbidden_mutations_leave_the_data_untouched()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        h.Db.CurrentUser.ActAsRespondent();

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.DeleteAsync(saved.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.ChangeStatusAsync(saved.Id, SurveyStatus.Published));

        h.Db.CurrentUser.ActAsAdmin();
        var current = await h.Service.GetAsync(saved.Id);
        Assert.Equal(SurveyStatus.Draft, current.Status);
    }
}
