using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>Tests of <see cref="UiFormat"/> helpers used across the UI.</summary>
public sealed class UiFormatTests
{
    [Theory]
    [InlineData("Jane Doe", "JD")]
    [InlineData("jane.doe@example.com", "jd")]
    [InlineData("admin@smartsurvey.local", "ad")]
    [InlineData("Ann", "An")]
    [InlineData("A", "A")]
    [InlineData("@example.com", "?")]
    [InlineData("  ", "?")]
    [InlineData(null, "?")]
    public void Initials_are_two_letters_of_the_name_or_e_mail(string? name, string expected) =>
        Assert.Equal(expected, UiFormat.Initials(name));
}
