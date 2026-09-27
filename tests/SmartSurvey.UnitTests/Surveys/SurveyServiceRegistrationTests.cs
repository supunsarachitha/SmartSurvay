using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Application.Surveys.Validation;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary>The service and its validator resolve from the Application DI registrations.</summary>
public class SurveyServiceRegistrationTests
{
    [Fact]
    public async Task Survey_service_resolves_with_the_application_registrations()
    {
        await using var db = new SqliteTestDatabase();
        var services = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddSingleton<IAppDbContextFactory>(db)
            .AddSingleton<ICurrentUser>(db.CurrentUser);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<SurveyService>(scope.ServiceProvider.GetRequiredService<ISurveyService>());
        Assert.IsType<SurveyDefinitionValidator>(scope.ServiceProvider.GetRequiredService<IValidator<SurveyDefinitionDto>>());
    }

    [Fact]
    public async Task Resolved_service_works_end_to_end()
    {
        await using var db = new SqliteTestDatabase();
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddSingleton<IAppDbContextFactory>(db)
            .AddSingleton<ICurrentUser>(db.CurrentUser)
            .BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ISurveyService>();

        var created = await service.CreateAsync(SampleSurveys.CustomerFeedback().Definition);

        Assert.Equal("customer-feedback", created.Slug);
    }
}
