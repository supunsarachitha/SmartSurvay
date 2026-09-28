using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;
using SmartSurvey.Infrastructure.Security;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="ProofOfWorkBotProtection"/> and its use by <see cref="ResponseService"/>.</summary>
public class BotProtectionTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _surveyId = Guid.NewGuid();

    private ProofOfWorkBotProtection Create(Action<BotProtectionOptions>? configure = null)
    {
        var options = new BotProtectionOptions { Difficulty = 500, MinimumSeconds = 3 };
        configure?.Invoke(options);
        return new ProofOfWorkBotProtection(new EphemeralDataProtectionProvider(), _time, Options.Create(options));
    }

    internal static BotChallengeSolution Solve(BotChallengeDto challenge)
    {
        for (long n = 0; n <= challenge.MaxNumber; n++)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(challenge.Salt + n.ToString(CultureInfo.InvariantCulture)))).ToLowerInvariant();
            if (hash == challenge.Challenge)
            {
                return new BotChallengeSolution { Salt = challenge.Salt, Challenge = challenge.Challenge, Signature = challenge.Signature, Number = n };
            }
        }

        throw new InvalidOperationException("No solution.");
    }

    [Fact]
    public void A_solved_challenge_passes_after_the_minimum_time()
    {
        var bot = Create();
        var challenge = bot.CreateChallenge(_surveyId);
        var solution = Solve(challenge);

        Assert.Equal("SHA-256", challenge.Algorithm);
        Assert.Equal(500, challenge.MaxNumber);
        Assert.StartsWith($"{_surveyId:N}.", challenge.Salt);

        Assert.False(bot.Check(_surveyId, solution, null).Passed); // answered in 0 seconds
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.True(bot.Check(_surveyId, solution, null).Passed);
    }

    [Fact]
    public void Wrong_tampered_foreign_or_missing_solutions_fail()
    {
        var bot = Create();
        var challenge = bot.CreateChallenge(_surveyId);
        var solution = Solve(challenge);
        _time.Advance(TimeSpan.FromMinutes(1));

        var wrongNumber = Solve(challenge); wrongNumber.Number = (wrongNumber.Number + 1) % 501;
        var forgedSignature = Solve(challenge); forgedSignature.Signature = "forged";
        var otherSalt = Solve(challenge); otherSalt.Salt = otherSalt.Salt.Replace('.', '-');

        Assert.False(bot.Check(_surveyId, wrongNumber, null).Passed);
        Assert.False(bot.Check(_surveyId, forgedSignature, null).Passed);
        Assert.False(bot.Check(_surveyId, otherSalt, null).Passed);
        Assert.False(bot.Check(Guid.NewGuid(), solution, null).Passed); // challenge of another survey
        Assert.False(bot.Check(_surveyId, null, null).Passed);
        Assert.False(bot.Check(_surveyId, solution, "https://spam.example").Passed); // honeypot
        Assert.False(Create().Check(_surveyId, solution, null).Passed); // signed by another key ring
    }

    [Fact]
    public void Challenges_expire_and_can_be_used_only_once()
    {
        var bot = Create(o => o.ChallengeLifetimeHours = 1);
        var solution = Solve(bot.CreateChallenge(_surveyId));
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.True(bot.TryConsume(solution));
        Assert.False(bot.TryConsume(solution));
        Assert.False(bot.Check(_surveyId, solution, null).Passed); // replay

        var late = Solve(bot.CreateChallenge(_surveyId));
        _time.Advance(TimeSpan.FromHours(2));
        Assert.Contains("open for a long time", bot.Check(_surveyId, late, null).Message);
    }

    [Fact]
    public void Disabled_protection_accepts_everything()
    {
        var bot = Create(o => o.Enabled = false);

        Assert.False(bot.IsEnabled);
        Assert.True(bot.Check(_surveyId, null, "filled").Passed);
    }

    [Fact]
    public void Server_hash_matches_the_browser_algorithm() =>
        // Test vector shared with js/pow-worker.js (verified independently with Python's hashlib and Node).
        Assert.Equal("39820a3a5c3e6cd21b65e808c6570470a7f45acec152d32b0f6a3d3326dce511", ProofOfWorkBotProtection.Hash("3f2a.1790000000.c0ffee", 4711));

    [Fact]
    public async Task Anonymous_submissions_are_checked_but_signed_in_ones_are_not()
    {
        var bot = Create(o => o.MinimumSeconds = 0);
        await using var h = await ResponseTestHarness.CreateAsync(bot);
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);
        Assert.NotNull(session.Challenge);
        var answers = new SaveResponseRequest
        {
            Answers =
            [
                new AnswerInputDto { QuestionId = s.Enjoy.Id, Selections = [new SelectionInputDto { OptionId = s.Yes.Id }] },
                new AnswerInputDto { QuestionId = s.Rating.Id, Number = 5 },
            ],
        };

        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.SubmitAsync(s.Definition.Id, answers));

        answers.Challenge = Solve(session.Challenge!);
        Assert.NotEqual(Guid.Empty, (await h.Service.SubmitAsync(s.Definition.Id, answers)).ResponseId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.SubmitAsync(s.Definition.Id, answers)); // replay

        h.User.ActAsRespondent();
        Assert.Null((await h.Service.StartOrResumeAsync(s.Definition.Slug!)).Challenge);
        answers.Challenge = null;
        Assert.NotEqual(Guid.Empty, (await h.Service.SubmitAsync(s.Definition.Id, answers)).ResponseId);
    }

    [Fact]
    public async Task Fixing_validation_errors_does_not_need_a_new_challenge()
    {
        var bot = Create(o => o.MinimumSeconds = 0);
        await using var h = await ResponseTestHarness.CreateAsync(bot);
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();
        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);
        var request = new SaveResponseRequest { Challenge = Solve(session.Challenge!) };

        await Assert.ThrowsAsync<AppValidationException>(() => h.Service.SubmitAsync(s.Definition.Id, request)); // required answers missing

        request.Answers =
        [
            new AnswerInputDto { QuestionId = s.Enjoy.Id, Selections = [new SelectionInputDto { OptionId = s.Yes.Id }] },
            new AnswerInputDto { QuestionId = s.Rating.Id, Number = 4 },
        ];
        Assert.NotEqual(Guid.Empty, (await h.Service.SubmitAsync(s.Definition.Id, request)).ResponseId);
    }
}
