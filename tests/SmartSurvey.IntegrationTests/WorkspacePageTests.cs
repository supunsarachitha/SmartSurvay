using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using static SmartSurvey.IntegrationTests.ApiTestData;

namespace SmartSurvey.IntegrationTests;

/// <summary>The public workspace pages over HTTP (static SSR): sign-up, join, workspace page, unavailable page.</summary>
[Collection(ApiCollection.Name)]
public sealed partial class WorkspacePageTests(ApiFactory factory)
{
    [Fact]
    public async Task Sign_up_page_offers_to_create_a_workspace()
    {
        var html = await factory.Anonymous().GetStringAsync("/signup");

        Assert.Contains("Create your workspace", html);
        Assert.Contains("name=\"Input.WorkspaceName\"", html);
    }

    [Fact]
    public async Task Register_page_without_a_workspace_explains_the_two_ways_in()
    {
        var html = await factory.Anonymous().GetStringAsync("/Account/Register");

        Assert.Contains("How would you like to start?", html);
        Assert.Contains("href=\"signup\"", html);
        Assert.Contains("name=\"workspace\"", html);
    }

    [Fact]
    public async Task Register_page_with_a_workspace_is_its_join_form()
    {
        var html = await factory.Anonymous().GetStringAsync($"/Account/Register?workspace={ApiFactory.WorkspaceSlug}");
        Assert.Contains("Join Default workspace", html);

        var unknown = await factory.Anonymous().GetStringAsync("/Account/Register?workspace=no-such-team");
        Assert.Contains("Workspace not found", unknown);
    }

    [Fact]
    public async Task Workspace_page_lists_its_public_surveys_and_invites_guests_to_join()
    {
        var survey = await CreatePublishedSurveyAsync(factory.Admin(), "Listed on the workspace page");

        var html = await factory.Anonymous().GetStringAsync($"/w/{ApiFactory.WorkspaceSlug}");

        Assert.Contains("Default workspace", html);
        Assert.Contains($"href=\"s/{survey.Slug}\"", html);
        Assert.Contains($"Account/Register?workspace={ApiFactory.WorkspaceSlug}", html);
        Assert.Contains("Workspace not found", await factory.Anonymous().GetStringAsync("/w/no-such-team"));
    }

    [Fact]
    public async Task Workspace_unavailable_page_explains_the_reason()
    {
        Assert.Contains("waiting for approval", await factory.Anonymous().GetStringAsync("/workspace-unavailable?reason=pending"));
        Assert.Contains("Your workspace is unavailable", await factory.Anonymous().GetStringAsync("/workspace-unavailable"));
    }

    [Fact]
    public async Task Signing_up_through_the_form_signs_the_founder_in_to_their_new_workspace()
    {
        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var form = await browser.GetStringAsync("/signup");

        var response = await browser.PostAsync("/signup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "signup",
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(form).Groups[1].Value,
            ["Input.WorkspaceName"] = $"Form Team {suffix}",
            ["Input.WorkspaceSlug"] = "",
            ["Input.DisplayName"] = "Form Founder",
            ["Input.Email"] = $"form-{suffix}@it.local",
            ["Input.Password"] = "FormTeam123!",
            ["Input.ConfirmPassword"] = "FormTeam123!",
        }));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Redirect,
            $"expected a redirect, got {(int)response.StatusCode}: {string.Join(" | ", FormMessages().Matches(body).Select(m => m.Groups[1].Value))}");
        Assert.EndsWith("/admin", response.Headers.Location!.OriginalString);

        var dashboard = await browser.GetAsync("/admin");
        await AssertStatusAsync(dashboard, HttpStatusCode.OK);
        Assert.Contains($"Form Team {suffix}", await dashboard.Content.ReadAsStringAsync()); // the sidebar shows the new workspace
    }

    [GeneratedRegex("class=\"(?:validation-message|alert[^\"]*)\"[^>]*>([^<]{3,})")]
    private static partial Regex FormMessages();

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
