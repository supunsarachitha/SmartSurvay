using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Reports.Charts;
using SmartSurvey.Application.Responses;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>
/// bUnit context with the services the public pages need: branding ("Acme Surveys"), support
/// options, a settable current user, chart renderer, loose JS interop, test authorization and a
/// <see cref="FakeResponseService"/>.
/// </summary>
public abstract class UiTestBase : BunitContext
{
    protected UiTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IBrandingService>(new StubBrandingService(ProductName));
        Services.AddSingleton<IOptions<SupportOptions>>(_ => Options.Create(Support));
        Services.AddSingleton<ICurrentUser>(User);
        Services.AddSingleton<ISvgChartRenderer, SvgChartRenderer>();
        Services.AddSingleton<IResponseService>(Responses);
        Services.AddScoped<BrowserInterop>();
        Services.AddSingleton(TimeProvider.System);
        Services.AddScoped<SubmissionThrottle>();
        Auth = AddAuthorization();
    }

    protected const string ProductName = "Acme Surveys";

    protected SupportOptions Support { get; } = new() { BuyMeACoffeeUsername = "acme", GitHubUrl = null };

    protected TestCurrentUser User { get; } = TestCurrentUser.Anonymous();

    protected FakeResponseService Responses { get; } = new();

    protected BunitAuthorizationContext Auth { get; }

    /// <summary>Signs in as a respondent (current user and authorization state).</summary>
    protected void SignInAsRespondent()
    {
        User.ActAsRespondent();
        Auth.SetAuthorized("user@test.local");
    }

    /// <summary>Signs in as an administrator (current user and authorization state).</summary>
    protected void SignInAsAdmin()
    {
        User.ActAsAdmin();
        Auth.SetAuthorized("admin@test.local");
        Auth.SetRoles(Domain.Identity.AppRoles.Admin);
    }

    /// <summary>Sets the current URL (for <c>[SupplyParameterFromQuery]</c> parameters).</summary>
    protected void NavigateTo(string relativeUrl) => Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUrl);
}
