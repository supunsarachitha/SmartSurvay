using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Infrastructure.Security;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary>Password-protected surveys: hashing, access keys and the survey-design rules.</summary>
public sealed class SurveyPasswordTests
{
    [Fact]
    public void Hashes_are_salted_versioned_and_verifiable()
    {
        var first = SurveyPasswordHasher.Hash("open sesame");
        var second = SurveyPasswordHasher.Hash("open sesame");

        Assert.StartsWith("v1.100000.", first);
        Assert.NotEqual(first, second); // random salt
        Assert.DoesNotContain("open sesame", first);
        Assert.True(SurveyPasswordHasher.Verify("open sesame", first));
        Assert.True(SurveyPasswordHasher.Verify("open sesame", second));
        Assert.False(SurveyPasswordHasher.Verify("Open sesame", first));
        Assert.False(SurveyPasswordHasher.Verify("", first));
        Assert.False(SurveyPasswordHasher.Verify("open sesame", null));
        Assert.False(SurveyPasswordHasher.Verify("open sesame", "v1.100000.not-base64.x"));
        Assert.False(SurveyPasswordHasher.Verify("open sesame", "plain text"));
    }

    [Fact]
    public void Access_keys_are_bound_to_survey_password_and_time()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
        var keys = new DataProtectionSurveyAccessKeys(new EphemeralDataProtectionProvider(), time);
        var surveyId = Guid.NewGuid();
        var hash = SurveyPasswordHasher.Hash("secret");

        var key = keys.Issue(surveyId, hash);

        Assert.True(keys.IsValid(key, surveyId, hash));
        Assert.False(keys.IsValid(key, Guid.NewGuid(), hash)); // other survey
        Assert.False(keys.IsValid(key, surveyId, SurveyPasswordHasher.Hash("secret"))); // password changed (new hash)
        Assert.False(keys.IsValid(key + "x", surveyId, hash)); // tampered
        Assert.False(keys.IsValid(null, surveyId, hash));
        Assert.False(new DataProtectionSurveyAccessKeys(new EphemeralDataProtectionProvider(), time).IsValid(key, surveyId, hash)); // other key ring

        time.Advance(keys.Lifetime);
        Assert.False(keys.IsValid(key, surveyId, hash)); // expired
    }

    [Fact]
    public async Task Creating_a_protected_survey_stores_only_a_hash()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Definition.PasswordProtected = true;
        sample.Definition.AccessPassword = "team-2026";

        var saved = await h.Service.CreateAsync(sample.Definition);
        var entity = await h.LoadSurveyAsync(saved.Id);

        Assert.True(saved.PasswordProtected);
        Assert.Null(saved.AccessPassword); // never returned
        Assert.True(SurveyPasswordHasher.Verify("team-2026", entity.AccessPasswordHash));
        Assert.True((await h.Service.ListAsync(new SurveyQuery())).Items.Single().IsPasswordProtected);
    }

    [Fact]
    public async Task Switching_protection_on_requires_a_password_of_reasonable_length()
    {
        await using var h = new SurveyServiceHarness();
        var missing = SampleSurveys.CustomerFeedback();
        missing.Definition.PasswordProtected = true;
        var tooShort = SampleSurveys.CustomerFeedback("Short");
        tooShort.Definition.PasswordProtected = true;
        tooShort.Definition.AccessPassword = "abc";

        var noPassword = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.CreateAsync(missing.Definition));
        var shortPassword = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.CreateAsync(tooShort.Definition));

        Assert.Equal(["AccessPassword"], noPassword.Errors.Keys);
        Assert.Contains("AccessPassword", shortPassword.Errors.Keys);
    }

    [Fact]
    public async Task Updates_keep_change_or_remove_the_password()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Definition.PasswordProtected = true;
        sample.Definition.AccessPassword = "first-pass";
        var saved = await h.Service.CreateAsync(sample.Definition);
        var originalHash = (await h.LoadSurveyAsync(saved.Id)).AccessPasswordHash;

        saved.Title = "Renamed"; // PasswordProtected true, no new password → keep
        saved = await h.Service.UpdateAsync(saved.Id, saved);
        Assert.Equal(originalHash, (await h.LoadSurveyAsync(saved.Id)).AccessPasswordHash);

        saved.AccessPassword = "second-pass";
        saved = await h.Service.UpdateAsync(saved.Id, saved);
        Assert.True(SurveyPasswordHasher.Verify("second-pass", (await h.LoadSurveyAsync(saved.Id)).AccessPasswordHash));

        saved.PasswordProtected = false;
        saved = await h.Service.UpdateAsync(saved.Id, saved);
        Assert.False(saved.PasswordProtected);
        Assert.Null((await h.LoadSurveyAsync(saved.Id)).AccessPasswordHash);
    }

    [Fact]
    public async Task Duplicates_keep_the_password_but_exports_and_imports_never_carry_one()
    {
        await using var h = new SurveyServiceHarness();
        var sample = SampleSurveys.CustomerFeedback();
        sample.Definition.PasswordProtected = true;
        sample.Definition.AccessPassword = "shared-pass";
        var saved = await h.Service.CreateAsync(sample.Definition);

        var copy = await h.Service.DuplicateAsync(saved.Id, new DuplicateSurveyRequest());
        Assert.True(copy.PasswordProtected);
        Assert.True(SurveyPasswordHasher.Verify("shared-pass", (await h.LoadSurveyAsync(copy.Id)).AccessPasswordHash));

        var document = await h.Service.ExportDefinitionAsync(saved.Id);
        Assert.False(document.Survey.PasswordProtected);

        document.Survey.PasswordProtected = true; // a hand-edited file without a password
        var imported = await h.Service.ImportDefinitionAsync(document);
        Assert.False(imported.PasswordProtected);
    }
}
