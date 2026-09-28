using Microsoft.AspNetCore.Identity;
using SmartSurvey.Application.Common;

namespace SmartSurvey.Infrastructure.Identity;

/// <summary>Translates failed <see cref="IdentityResult"/>s into application exceptions.</summary>
internal static class IdentityErrors
{
    /// <summary>
    /// Duplicates and concurrency failures become conflicts, password / e-mail problems validation
    /// errors keyed <paramref name="passwordKey"/> / <paramref name="emailKey"/>, anything else a
    /// business-rule violation.
    /// </summary>
    public static void ThrowIfFailed(IdentityResult result, string emailKey = "Email", string passwordKey = "Password")
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = result.Errors.ToList();
        var conflict = errors.FirstOrDefault(e => e.Code is "DuplicateEmail" or "DuplicateUserName" or "ConcurrencyFailure");
        if (conflict is not null)
        {
            throw new ConflictException(conflict.Description);
        }

        // Error codes: see IdentityErrorDescriber.
        var validation = errors
            .Select(e => (Key: e.Code switch
            {
                _ when e.Code.StartsWith("Password", StringComparison.Ordinal) => passwordKey,
                "InvalidEmail" or "InvalidUserName" => emailKey,
                _ => null,
            }, e.Description))
            .Where(e => e.Key is not null)
            .GroupBy(e => e.Key!)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).Distinct().ToArray());
        if (validation.Count > 0)
        {
            throw new AppValidationException(validation);
        }

        throw new BusinessRuleException(string.Join(" ", errors.Select(e => e.Description)));
    }
}
