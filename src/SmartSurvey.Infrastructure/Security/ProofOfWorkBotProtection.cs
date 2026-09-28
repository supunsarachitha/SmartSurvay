using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using SmartSurvey.Application.Responses;

namespace SmartSurvey.Infrastructure.Security;

/// <summary>"BotProtection" configuration section.</summary>
public sealed class BotProtectionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "BotProtection";

    /// <summary>Check anonymous submissions (default: true).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Upper bound of the proof-of-work search. The browser needs on average half of it in SHA-256 hashes —
    /// the default (50,000) takes a fraction of a second in a background thread.
    /// </summary>
    public int Difficulty { get; set; } = 50_000;

    /// <summary>Submissions faster than this after opening the survey are treated as automated.</summary>
    public int MinimumSeconds { get; set; } = 3;

    /// <summary>How long a challenge stays valid (long surveys, breaks).</summary>
    public int ChallengeLifetimeHours { get; set; } = 24;
}

/// <summary>
/// <see cref="IBotProtection"/> without third-party services: an ALTCHA-style proof-of-work challenge signed with
/// ASP.NET Core Data Protection, a minimum answering time, a honeypot field and one-time use of every challenge.
/// </summary>
/// <remarks>
/// The salt is <c>{surveyId}.{issuedUnixSeconds}.{random}</c>; the target hash covers it, so it cannot be changed
/// without invalidating the solution. Used challenges are remembered in memory until they expire; with several
/// application instances a challenge could be replayed once per instance, which is acceptable for spam protection.
/// </remarks>
public sealed class ProofOfWorkBotProtection(IDataProtectionProvider provider, TimeProvider time, IOptions<BotProtectionOptions> options)
    : IBotProtection
{
    private const string Algorithm = "SHA-256";
    private readonly IDataProtector _protector = provider.CreateProtector("SmartSurvey.BotChallenge.v1");
    private readonly ConcurrentDictionary<string, DateTimeOffset> _used = new(StringComparer.Ordinal);
    private int _consumeCount;

    private BotProtectionOptions Options => options.Value;

    /// <inheritdoc />
    public bool IsEnabled => Options.Enabled;

    /// <inheritdoc />
    public BotChallengeDto CreateChallenge(Guid surveyId)
    {
        var maxNumber = Math.Max(1, Options.Difficulty);
        var salt = string.Create(CultureInfo.InvariantCulture,
            $"{surveyId:N}.{time.GetUtcNow().ToUnixTimeSeconds()}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}");
        var challenge = Hash(salt, RandomNumberGenerator.GetInt32(0, maxNumber + 1));
        return new BotChallengeDto(Algorithm, salt, challenge, maxNumber, _protector.Protect(challenge));
    }

    /// <inheritdoc />
    public BotCheckResult Check(Guid surveyId, BotChallengeSolution? solution, string? honeypot)
    {
        if (!Options.Enabled)
        {
            return BotCheckResult.Ok;
        }

        const string reload = "We couldn't confirm that this response was sent by a person. Please reload the page and submit it again.";
        if (!string.IsNullOrWhiteSpace(honeypot) || solution is null || string.IsNullOrEmpty(solution.Salt) || string.IsNullOrEmpty(solution.Challenge))
        {
            return new BotCheckResult(false, reload);
        }

        if (!IsSignedChallenge(solution) || solution.Number < 0 || solution.Number > Math.Max(1, Options.Difficulty)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(solution.Salt, solution.Number)), Encoding.ASCII.GetBytes(solution.Challenge)))
        {
            return new BotCheckResult(false, reload);
        }

        var parts = solution.Salt.Split('.');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var saltSurvey) || saltSurvey != surveyId
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var issuedUnix))
        {
            return new BotCheckResult(false, reload);
        }

        var age = time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(issuedUnix);
        if (age < TimeSpan.FromSeconds(Options.MinimumSeconds))
        {
            return new BotCheckResult(false, "That was very quick! Please take a moment to check your answers, then submit again.");
        }

        if (age > TimeSpan.FromHours(Options.ChallengeLifetimeHours))
        {
            return new BotCheckResult(false, "This page has been open for a long time. Please reload it and submit your answers again.");
        }

        return _used.ContainsKey(solution.Challenge) ? new BotCheckResult(false, reload) : BotCheckResult.Ok;
    }

    /// <inheritdoc />
    public bool TryConsume(BotChallengeSolution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);
        var now = time.GetUtcNow();
        if (Interlocked.Increment(ref _consumeCount) % 1000 == 0)
        {
            foreach (var (challenge, expires) in _used)
            {
                if (expires < now)
                {
                    _used.TryRemove(challenge, out _);
                }
            }
        }

        return _used.TryAdd(solution.Challenge, now.AddHours(Options.ChallengeLifetimeHours));
    }

    /// <summary>Lower-case hex SHA-256 of <c>salt + number</c> — the same computation the browser performs.</summary>
    internal static string Hash(string salt, long number) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + number.ToString(CultureInfo.InvariantCulture)))).ToLowerInvariant();

    private bool IsSignedChallenge(BotChallengeSolution solution)
    {
        try
        {
            return string.Equals(_protector.Unprotect(solution.Signature), solution.Challenge, StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
