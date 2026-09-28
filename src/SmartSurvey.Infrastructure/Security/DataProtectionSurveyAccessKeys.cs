using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using SmartSurvey.Application.Common;

namespace SmartSurvey.Infrastructure.Security;

/// <summary>
/// <see cref="ISurveyAccessKeys"/> built on ASP.NET Core Data Protection: a key is the survey id plus a short
/// fingerprint of the current password hash, encrypted and signed with an expiry. Changing the survey password
/// changes the fingerprint, so keys issued for the old password stop working immediately. Keys come from the
/// application's key ring (see <c>DataProtection:KeysPath</c>), so they survive restarts and work across instances
/// that share the key ring.
/// </summary>
public sealed class DataProtectionSurveyAccessKeys(IDataProtectionProvider provider, TimeProvider time) : ISurveyAccessKeys
{
    // The expiry is part of the signed payload and checked against the application clock (testable, consistent).
    private readonly IDataProtector _protector = provider.CreateProtector("SmartSurvey.SurveyAccessKeys.v1");

    /// <inheritdoc />
    public TimeSpan Lifetime { get; } = TimeSpan.FromHours(12);

    /// <inheritdoc />
    public string Issue(Guid surveyId, string passwordHash)
    {
        var expires = time.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        return _protector.Protect($"{Subject(surveyId, passwordHash)}.{expires.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <inheritdoc />
    public bool IsValid(string? key, Guid surveyId, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(key);
        }
        catch (CryptographicException)
        {
            return false; // tampered, from another key ring or not a key at all
        }

        var separator = payload.LastIndexOf('.');
        if (separator < 0 || !long.TryParse(payload.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || DateTimeOffset.FromUnixTimeSeconds(expires) <= time.GetUtcNow())
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(payload[..separator]), Encoding.UTF8.GetBytes(Subject(surveyId, passwordHash)));
    }

    /// <summary>Survey id + fingerprint of the current password hash (a new password invalidates old keys).</summary>
    private static string Subject(Guid surveyId, string passwordHash) =>
        $"{surveyId:N}.{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)), 0, 16)}";
}
