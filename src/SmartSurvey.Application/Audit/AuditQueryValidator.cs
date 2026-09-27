using FluentValidation;

namespace SmartSurvey.Application.Audit;

/// <summary>Validates audit log filters before they are turned into a database query.</summary>
internal sealed class AuditQueryValidator : AbstractValidator<AuditQuery>
{
    /// <summary>Maximum length of the free-text search term.</summary>
    public const int MaxSearchLength = 200;

    /// <summary>Creates the validator.</summary>
    public AuditQueryValidator()
    {
        RuleFor(q => q.Search)
            .MaximumLength(MaxSearchLength)
            .WithMessage($"The search term must be at most {MaxSearchLength} characters.");

        RuleFor(q => q.EntityType)
            .MaximumLength(AuditService.MaxCodeLength)
            .WithMessage($"The entity type must be at most {AuditService.MaxCodeLength} characters.");

        RuleFor(q => q.EntityId)
            .MaximumLength(AuditService.MaxCodeLength)
            .WithMessage($"The entity id must be at most {AuditService.MaxCodeLength} characters.");

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .When(q => q.From.HasValue && q.To.HasValue)
            .WithMessage("The end date must be on or after the start date.");
    }
}
