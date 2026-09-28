using Bunit;
using SmartSurvey.Web.Components.Pages;
using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>The user guide page and the project links (GitHub, Buy Me a Coffee).</summary>
public sealed class GuidePageTests : UiTestBase
{
    [Fact]
    public void Every_contents_entry_points_to_a_chapter()
    {
        var page = Render<Guide>();

        var ids = page.FindAll("section.guide-section").Select(s => s.Id ?? string.Empty).ToList();
        var anchors = Guide.Contents.SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(anchors, ids);
        Assert.All(anchors, id => Assert.NotNull(page.Find($".guide-toc a[href='guide#{id}']")));
    }

    [Fact]
    public void Guide_uses_the_product_name_and_describes_every_screenshot()
    {
        var page = Render<Guide>();

        Assert.Equal($"How to use {ProductName}", page.Find("h1").TextContent.Trim());
        var images = page.FindAll("img");
        Assert.NotEmpty(images);
        Assert.All(images, img =>
        {
            Assert.StartsWith("img/guide/", img.GetAttribute("src"));
            Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("alt")));
        });
    }

    [Fact]
    public void Guide_links_to_github_only_when_configured()
    {
        Assert.Empty(Render<Guide>().FindAll("a[href^='https://github.com']"));

        Support.GitHubUrl = "https://github.com/acme/surveys";

        Assert.NotNull(Render<Guide>().Find("a[href='https://github.com/acme/surveys']"));
    }

    [Fact]
    public void Project_links_default_to_the_owners_accounts()
    {
        var defaults = new SupportOptions();

        Assert.Equal("https://buymeacoffee.com/jkhy9gtjs", defaults.BuyMeACoffeeUrl);
        Assert.Equal("https://github.com/supunsarachitha/SmartSurvay", defaults.GitHubUrl);
    }
}
