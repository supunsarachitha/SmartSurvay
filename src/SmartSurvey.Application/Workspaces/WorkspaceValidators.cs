using FluentValidation;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Application.Workspaces;

/// <summary>Rules for workspace addresses (<c>/w/{slug}</c>): lower-case letters, digits and hyphens, 3–60 characters.</summary>
public static class WorkspaceSlugs
{
    /// <summary>Shortest allowed slug.</summary>
    public const int MinLength = 3;

    /// <summary>Message for an invalid slug.</summary>
    public const string InvalidMessage =
        "Use 3–60 lower-case letters, digits and hyphens (no hyphen at the start or end), e.g. \"acme-research\".";

    /// <summary>True for a well-formed slug.</summary>
    public static bool IsValid(string? slug) =>
        slug is { Length: >= MinLength and <= Workspace.SlugMaxLength } && SlugGenerator.IsValid(slug);

    /// <summary>The requested slug (trimmed, lower case) or one generated from <paramref name="name"/>.</summary>
    public static string FromRequestOrName(string? requested, string name)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested.Trim().ToLowerInvariant();
        }

        var slug = SlugGenerator.Generate(name);
        if (slug.Length > Workspace.SlugMaxLength)
        {
            slug = slug[..Workspace.SlugMaxLength].Trim('-');
        }

        return slug.Length >= MinLength ? slug : (slug + "-workspace").Trim('-');
    }

    /// <summary><paramref name="slug"/> with a numeric suffix, still within the length limit.</summary>
    public static string WithSuffix(string slug, int suffix)
    {
        var tail = "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var head = slug.Length + tail.Length > Workspace.SlugMaxLength ? slug[..(Workspace.SlugMaxLength - tail.Length)].Trim('-') : slug;
        return head + tail;
    }
}

/// <summary>Shared rule sets.</summary>
internal static class WorkspaceRules
{
    public const int MaxDescriptionLength = 2000;
    public const int MaxEmailLength = 256;
    public const int MaxDisplayNameLength = 200;

    public static IRuleBuilderOptions<T, string> WorkspaceName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Please enter a name for the workspace.")
            .MaximumLength(Workspace.NameMaxLength).WithMessage($"Use at most {Workspace.NameMaxLength} characters.");

    public static IRuleBuilderOptions<T, string?> OptionalEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(MaxEmailLength).EmailAddress().WithMessage("Please enter a valid e-mail address.");

    public static IRuleBuilderOptions<T, string> RequiredEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(e => !string.IsNullOrWhiteSpace(e)).WithMessage("Please enter an e-mail address.")
            .MaximumLength(MaxEmailLength).EmailAddress().WithMessage("Please enter a valid e-mail address.");

    public static IRuleBuilderOptions<T, string?> Slug<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(s => string.IsNullOrWhiteSpace(s) || WorkspaceSlugs.IsValid(s.Trim().ToLowerInvariant())).WithMessage(WorkspaceSlugs.InvalidMessage);
}

/// <summary>Validates <see cref="UpdateWorkspaceSettingsRequest"/>.</summary>
public sealed class UpdateWorkspaceSettingsRequestValidator : AbstractValidator<UpdateWorkspaceSettingsRequest>
{
    /// <summary>Creates the validator.</summary>
    public UpdateWorkspaceSettingsRequestValidator()
    {
        RuleFor(x => x.Name).WorkspaceName();
        RuleFor(x => x.Description).MaximumLength(WorkspaceRules.MaxDescriptionLength);
        RuleFor(x => x.ContactEmail).OptionalEmail();
    }
}

/// <summary>Validates <see cref="UpdateWorkspaceRequest"/>.</summary>
public sealed class UpdateWorkspaceRequestValidator : AbstractValidator<UpdateWorkspaceRequest>
{
    /// <summary>Creates the validator.</summary>
    public UpdateWorkspaceRequestValidator()
    {
        RuleFor(x => x.Name).WorkspaceName();
        RuleFor(x => x.Slug).Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage("Please enter an address.");
        RuleFor(x => (string?)x.Slug).Slug().OverridePropertyName(nameof(UpdateWorkspaceRequest.Slug));
        RuleFor(x => x.Description).MaximumLength(WorkspaceRules.MaxDescriptionLength);
        RuleFor(x => x.ContactEmail).OptionalEmail();
    }
}

/// <summary>Validates <see cref="CreateWorkspaceRequest"/> (the password policy is enforced by Identity).</summary>
public sealed class CreateWorkspaceRequestValidator : AbstractValidator<CreateWorkspaceRequest>
{
    /// <summary>Creates the validator.</summary>
    public CreateWorkspaceRequestValidator()
    {
        RuleFor(x => x.Name).WorkspaceName();
        RuleFor(x => x.Slug).Slug();
        RuleFor(x => x.AdminEmail).RequiredEmail();
        RuleFor(x => x.AdminDisplayName).MaximumLength(WorkspaceRules.MaxDisplayNameLength);
        RuleFor(x => x.AdminPassword).NotEmpty().WithMessage("Please enter a password.");
    }
}

/// <summary>Validates <see cref="WorkspaceSignupRequest"/> (the password policy is enforced by Identity).</summary>
public sealed class WorkspaceSignupRequestValidator : AbstractValidator<WorkspaceSignupRequest>
{
    /// <summary>Creates the validator.</summary>
    public WorkspaceSignupRequestValidator()
    {
        RuleFor(x => x.WorkspaceName).WorkspaceName();
        RuleFor(x => x.WorkspaceSlug).Slug();
        RuleFor(x => x.DisplayName).MaximumLength(WorkspaceRules.MaxDisplayNameLength);
        RuleFor(x => x.Email).RequiredEmail();
        RuleFor(x => x.Password).NotEmpty().WithMessage("Please enter a password.");
    }
}

/// <summary>Validates <see cref="JoinWorkspaceRequest"/> (the password policy is enforced by Identity).</summary>
public sealed class JoinWorkspaceRequestValidator : AbstractValidator<JoinWorkspaceRequest>
{
    /// <summary>Creates the validator.</summary>
    public JoinWorkspaceRequestValidator()
    {
        RuleFor(x => x.DisplayName).MaximumLength(WorkspaceRules.MaxDisplayNameLength);
        RuleFor(x => x.Email).RequiredEmail();
        RuleFor(x => x.Password).NotEmpty().WithMessage("Please enter a password.");
    }
}

/// <summary>Validates <see cref="UpdatePlatformSettingsRequest"/>.</summary>
public sealed class UpdatePlatformSettingsRequestValidator : AbstractValidator<UpdatePlatformSettingsRequest>
{
    /// <summary>Creates the validator.</summary>
    public UpdatePlatformSettingsRequestValidator() => RuleFor(x => x.SupportEmail).OptionalEmail();
}
