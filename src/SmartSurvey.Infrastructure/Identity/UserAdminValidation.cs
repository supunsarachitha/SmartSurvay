using FluentValidation;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Users;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Infrastructure.Identity;

/// <summary>Case-insensitive helpers for the fixed role set <see cref="AppRoles.All"/>.</summary>
internal static class AppRoleNames
{
    /// <summary>True when <paramref name="role"/> names one of <see cref="AppRoles.All"/> (case-insensitive).</summary>
    public static bool IsKnown(string? role) =>
        !string.IsNullOrWhiteSpace(role)
        && AppRoles.All.Any(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Role names of <paramref name="roles"/> that are not part of <see cref="AppRoles.All"/>.</summary>
    public static IReadOnlyList<string> Unknown(IEnumerable<string?>? roles) =>
        (roles ?? []).Where(r => !IsKnown(r)).Select(r => r?.Trim() ?? string.Empty).Distinct().ToList();

    /// <summary>
    /// Maps known role names to their canonical spelling, removes duplicates and returns them in the
    /// order of <see cref="AppRoles.All"/>. Unknown names are ignored (validate first).
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string?> roles)
    {
        var requested = new HashSet<string>(
            roles.Where(IsKnown).Select(r => r!.Trim()),
            StringComparer.OrdinalIgnoreCase);
        return AppRoles.All.Where(requested.Contains).ToList();
    }

    /// <summary>User-facing error for unknown roles.</summary>
    public static string UnknownRolesMessage(IEnumerable<string> unknown)
    {
        var names = string.Join(", ", unknown.Select(r => r.Length == 0 ? "(empty)" : $"'{r}'"));
        return $"Unknown role {names}. Allowed roles: {string.Join(", ", AppRoles.All)}.";
    }
}

/// <summary>Validates <see cref="CreateUserRequest"/> (the password policy itself is enforced by Identity).</summary>
internal sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    /// <summary>Maximum e-mail length (Identity column size).</summary>
    public const int MaxEmailLength = 256;

    /// <summary>Maximum display-name length (column size).</summary>
    public const int MaxDisplayNameLength = 200;

    /// <summary>Creates the validator.</summary>
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("E-mail is required.")
            .MaximumLength(MaxEmailLength).WithMessage($"The e-mail must be at most {MaxEmailLength} characters.")
            .Must(e => ResponseValidator.IsValidEmail(e.Trim())).WithMessage("Please enter a valid e-mail address.");

        RuleFor(r => r.DisplayName)
            .MaximumLength(MaxDisplayNameLength)
            .WithMessage($"The display name must be at most {MaxDisplayNameLength} characters.");

        RuleFor(r => r.Password)
            .NotEmpty().WithMessage("Password is required.");

        RuleFor(r => r.Roles)
            .Must(roles => AppRoleNames.Unknown(roles).Count == 0)
            .WithMessage(r => AppRoleNames.UnknownRolesMessage(AppRoleNames.Unknown(r.Roles)));
    }
}

/// <summary>Validates <see cref="UserQuery"/> filters.</summary>
internal sealed class UserQueryValidator : AbstractValidator<UserQuery>
{
    /// <summary>Maximum length of the search term.</summary>
    public const int MaxSearchLength = 200;

    /// <summary>Creates the validator.</summary>
    public UserQueryValidator()
    {
        RuleFor(q => q.Search)
            .MaximumLength(MaxSearchLength)
            .WithMessage($"The search term must be at most {MaxSearchLength} characters.");

        RuleFor(q => q.Role)
            .Must(AppRoleNames.IsKnown)
            .When(q => !string.IsNullOrWhiteSpace(q.Role))
            .WithMessage(q => AppRoleNames.UnknownRolesMessage([q.Role!.Trim()]));
    }
}
