using SmartSurvey.Application.Common;

namespace SmartSurvey.UnitTests.Core;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Customer Satisfaction 2026", "customer-satisfaction-2026")]
    [InlineData("  Café  Menu!!  ", "cafe-menu")]
    [InlineData("---", "survey")]
    [InlineData(null, "survey")]
    [InlineData("A/B test: v2.0", "a-b-test-v2-0")]
    public void Generate_creates_url_friendly_slugs(string? input, string expected) =>
        Assert.Equal(expected, SlugGenerator.Generate(input));

    [Fact]
    public void Generate_truncates_to_max_length_without_trailing_hyphen()
    {
        var slug = SlugGenerator.Generate(new string('a', 79) + " bbbb");
        Assert.True(slug.Length <= SlugGenerator.MaxLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Theory]
    [InlineData("valid-slug-1", true)]
    [InlineData("Invalid", false)]
    [InlineData("double--hyphen", false)]
    [InlineData("-leading", false)]
    [InlineData("", false)]
    public void IsValid_detects_canonical_slugs(string slug, bool expected) =>
        Assert.Equal(expected, SlugGenerator.IsValid(slug));

    [Fact]
    public void WithSuffix_respects_max_length()
    {
        var slug = SlugGenerator.WithSuffix(new string('x', SlugGenerator.MaxLength), 12);
        Assert.EndsWith("-12", slug);
        Assert.True(slug.Length <= SlugGenerator.MaxLength);
    }
}
