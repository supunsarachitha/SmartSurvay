namespace SmartSurvey.Application.Responses;

/// <summary>
/// A proof-of-work challenge (ALTCHA-style) handed out with a survey session. The respondent's browser finds the
/// number <c>n</c> (0 ≤ n ≤ <see cref="MaxNumber"/>) for which <c>SHA-256(salt + n)</c>, as lower-case hex, equals
/// <see cref="Challenge"/>, and returns it with the submission. Trivial for one person, expensive at spam scale.
/// </summary>
/// <param name="Algorithm">Hash algorithm (always <c>SHA-256</c>).</param>
/// <param name="Salt">Salt (contains the survey and the issue time; must be echoed unchanged).</param>
/// <param name="Challenge">Target hash (lower-case hex).</param>
/// <param name="MaxNumber">Upper bound of the search.</param>
/// <param name="Signature">Server signature of the challenge (must be echoed unchanged).</param>
public sealed record BotChallengeDto(string Algorithm, string Salt, string Challenge, int MaxNumber, string Signature);

/// <summary>A solved <see cref="BotChallengeDto"/>, sent with an anonymous submission.</summary>
public sealed class BotChallengeSolution
{
    /// <summary>The challenge's salt.</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>The challenge's target hash.</summary>
    public string Challenge { get; set; } = string.Empty;

    /// <summary>The challenge's signature.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>The number found by the browser.</summary>
    public long Number { get; set; }
}

/// <summary>Outcome of a bot check.</summary>
/// <param name="Passed">True when the submission looks human.</param>
/// <param name="Message">User-facing explanation when it doesn't.</param>
public sealed record BotCheckResult(bool Passed, string? Message)
{
    /// <summary>The check passed.</summary>
    public static BotCheckResult Ok { get; } = new(true, null);
}

/// <summary>
/// Spam and bot protection for anonymous survey submissions: a proof-of-work challenge, a minimum answering time,
/// a hidden "honeypot" field and one-time use of every challenge. Signed-in respondents are not challenged.
/// </summary>
public interface IBotProtection
{
    /// <summary>False when bot protection is switched off in the configuration.</summary>
    bool IsEnabled { get; }

    /// <summary>Creates a challenge for a new anonymous session of <paramref name="surveyId"/>.</summary>
    BotChallengeDto CreateChallenge(Guid surveyId);

    /// <summary>Checks a submission (no side effects): honeypot, signature, solution, survey and timing.</summary>
    BotCheckResult Check(Guid surveyId, BotChallengeSolution? solution, string? honeypot);

    /// <summary>Marks the challenge as used; false when it was used before (replayed submission).</summary>
    bool TryConsume(BotChallengeSolution solution);
}

/// <summary>Bot protection that accepts everything (used when the feature is disabled and in tests).</summary>
public sealed class DisabledBotProtection : IBotProtection
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public BotChallengeDto CreateChallenge(Guid surveyId) => throw new InvalidOperationException("Bot protection is disabled.");

    /// <inheritdoc />
    public BotCheckResult Check(Guid surveyId, BotChallengeSolution? solution, string? honeypot) => BotCheckResult.Ok;

    /// <inheritdoc />
    public bool TryConsume(BotChallengeSolution solution) => true;
}
