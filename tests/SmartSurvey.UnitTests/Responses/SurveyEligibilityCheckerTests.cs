using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Pure rule-order and message tests of <see cref="SurveyEligibilityChecker"/>.</summary>
public class SurveyEligibilityCheckerTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Survey OpenSurvey() => new()
    {
        Title = "S",
        Slug = "s",
        Status = SurveyStatus.Published,
        AllowAnonymous = true,
    };

    [Fact]
    public void Open_survey_without_restrictions_is_eligible()
    {
        var verdict = SurveyEligibilityChecker.Check(OpenSurvey(), Now, new ParticipationFacts(100, false, false));

        Assert.True(verdict.IsEligible);
        Assert.Null(verdict.Message);
    }

    [Fact]
    public void Quota_applies_only_when_a_maximum_is_set()
    {
        var unlimited = OpenSurvey();
        var limited = OpenSurvey();
        limited.MaxResponses = 3;

        Assert.True(SurveyEligibilityChecker.Check(unlimited, Now, new ParticipationFacts(1_000, false, false)).IsEligible);
        Assert.True(SurveyEligibilityChecker.Check(limited, Now, new ParticipationFacts(2, false, false)).IsEligible);
        Assert.Equal(
            SurveyEligibility.QuotaReached,
            SurveyEligibilityChecker.Check(limited, Now, new ParticipationFacts(3, false, false)).Eligibility);
    }

    [Fact]
    public void Quota_is_checked_before_login()
    {
        var survey = OpenSurvey();
        survey.MaxResponses = 1;
        survey.AllowAnonymous = false;

        var verdict = SurveyEligibilityChecker.Check(survey, Now, new ParticipationFacts(1, false, false));

        Assert.Equal(SurveyEligibility.QuotaReached, verdict.Eligibility);
    }

    [Fact]
    public void Already_responded_requires_an_authenticated_user()
    {
        var survey = OpenSurvey();

        var guest = SurveyEligibilityChecker.Check(survey, Now, new ParticipationFacts(1, false, true));
        var user = SurveyEligibilityChecker.Check(survey, Now, new ParticipationFacts(1, true, true));

        Assert.Equal(SurveyEligibility.Eligible, guest.Eligibility);
        Assert.Equal(SurveyEligibility.AlreadyResponded, user.Eligibility);
    }

    [Fact]
    public void Closing_time_message_is_only_used_for_published_surveys_whose_window_ended()
    {
        var expired = OpenSurvey();
        expired.ClosesAt = Now.AddHours(-2);
        var manuallyClosed = OpenSurvey();
        manuallyClosed.Status = SurveyStatus.Closed;
        manuallyClosed.ClosesAt = Now.AddDays(5);

        var facts = new ParticipationFacts(0, false, false);

        Assert.Equal("This survey closed on 15 Jan 2026 08:00 UTC.", SurveyEligibilityChecker.Check(expired, Now, facts).Message);
        Assert.Equal(SurveyEligibilityChecker.ClosedMessage, SurveyEligibilityChecker.Check(manuallyClosed, Now, facts).Message);
    }

    [Fact]
    public void Utc_timestamps_are_formatted_with_the_invariant_culture()
    {
        Assert.Equal("1 Mar 2026 09:05 UTC", SurveyEligibilityChecker.FormatUtc(new DateTime(2026, 3, 1, 9, 5, 0, DateTimeKind.Utc)));
        Assert.Equal("31 Dec 2026 23:59 UTC", SurveyEligibilityChecker.FormatUtc(new DateTime(2026, 12, 31, 23, 59, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(SurveyStatus.Published, false, SurveyAvailability.Open)]
    [InlineData(SurveyStatus.Draft, false, SurveyAvailability.NotPublished)]
    [InlineData(SurveyStatus.Published, true, SurveyAvailability.NotPublished)]
    [InlineData(SurveyStatus.Closed, false, SurveyAvailability.Closed)]
    public void Availability_of_projected_columns_matches_the_domain_rule(SurveyStatus status, bool isTemplate, SurveyAvailability expected)
    {
        Assert.Equal(expected, SurveyEligibilityChecker.GetAvailability(status, isTemplate, null, null, Now));
    }

    [Fact]
    public void Availability_of_projected_columns_respects_the_schedule()
    {
        Assert.Equal(
            SurveyAvailability.NotOpenYet,
            SurveyEligibilityChecker.GetAvailability(SurveyStatus.Published, false, Now.AddMinutes(1), null, Now));
        Assert.Equal(
            SurveyAvailability.Closed,
            SurveyEligibilityChecker.GetAvailability(SurveyStatus.Published, false, null, Now, Now));
    }
}
