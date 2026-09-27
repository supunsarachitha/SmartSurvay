using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.UnitTests.Ui;

public sealed class EmbeddingOptionsTests
{
    [Fact]
    public void Any_site_may_embed_when_no_origins_are_configured() =>
        Assert.Equal("'self' *", new EmbeddingOptions().FrameAncestors);

    [Fact]
    public void Configured_origins_are_listed_and_invalid_ones_dropped()
    {
        var options = new EmbeddingOptions
        {
            AllowedOrigins = ["https://www.example.com", "http://intranet:8080", "https://evil.com/path", "javascript:alert(1)", "https://a.com\r\nX: y"],
        };

        Assert.Equal("'self' https://www.example.com http://intranet:8080", options.FrameAncestors);
    }
}
