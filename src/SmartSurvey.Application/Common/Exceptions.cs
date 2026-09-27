namespace SmartSurvey.Application.Common;

/// <summary>
/// Base type for expected, user-facing application errors. The REST API maps each subtype to a
/// ProblemDetails status code; Blazor pages display <see cref="Exception.Message"/> in an alert.
/// </summary>
public abstract class AppException : Exception
{
    /// <summary>Creates the exception.</summary>
    protected AppException(string message) : base(message)
    {
    }
}

/// <summary>Requested entity does not exist (HTTP 404).</summary>
public sealed class NotFoundException : AppException
{
    /// <summary>Creates the exception for an entity type and key.</summary>
    public NotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.")
    {
        EntityName = entityName;
        Key = key;
    }

    /// <summary>Entity type name.</summary>
    public string EntityName { get; }

    /// <summary>Requested key.</summary>
    public object Key { get; }
}

/// <summary>
/// Input failed validation (HTTP 400). <see cref="Errors"/> is keyed by property path
/// (e.g. <c>Title</c>, <c>Sections[0].Questions[2].Text</c>) or, for survey answers, by question id.
/// </summary>
public sealed class AppValidationException : AppException
{
    /// <summary>Creates the exception from a dictionary of errors.</summary>
    public AppValidationException(IDictionary<string, string[]> errors)
        : base(BuildMessage(errors))
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    /// <summary>Creates the exception for a single error.</summary>
    public AppValidationException(string key, string error)
        : this(new Dictionary<string, string[]> { [key] = [error] })
    {
    }

    /// <summary>Validation errors keyed by property path or question id.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    private static string BuildMessage(IDictionary<string, string[]> errors)
    {
        var first = errors.SelectMany(e => e.Value).FirstOrDefault();
        return errors.Count switch
        {
            0 => "One or more validation errors occurred.",
            _ when errors.Sum(e => e.Value.Length) == 1 && first is not null => first,
            _ => $"One or more validation errors occurred: {first}",
        };
    }
}

/// <summary>The current user may not perform the operation (HTTP 403).</summary>
public sealed class ForbiddenException : AppException
{
    /// <summary>Creates the exception.</summary>
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message)
    {
    }
}

/// <summary>State conflict such as a concurrency violation or duplicate slug (HTTP 409).</summary>
public sealed class ConflictException : AppException
{
    /// <summary>Creates the exception.</summary>
    public ConflictException(string message) : base(message)
    {
    }
}

/// <summary>A business rule prevents the operation, e.g. "survey is closed" (HTTP 422).</summary>
public sealed class BusinessRuleException : AppException
{
    /// <summary>Creates the exception.</summary>
    public BusinessRuleException(string message) : base(message)
    {
    }
}
