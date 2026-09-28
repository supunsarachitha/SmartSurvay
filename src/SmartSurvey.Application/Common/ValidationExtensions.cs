using FluentValidation;

namespace SmartSurvey.Application.Common;

/// <summary>FluentValidation helpers shared by the services.</summary>
public static class ValidationExtensions
{
    /// <summary>
    /// Validates <paramref name="instance"/> and throws <see cref="AppValidationException"/> keyed by
    /// property name when it is invalid.
    /// </summary>
    public static void ValidateOrThrow<T>(this IValidator<T> validator, T instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var result = validator.Validate(instance);
        if (!result.IsValid)
        {
            throw new AppValidationException(result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()));
        }
    }
}
