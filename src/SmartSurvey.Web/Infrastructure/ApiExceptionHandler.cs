using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartSurvey.Application.Common;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// Converts exceptions thrown by API endpoints into RFC 7807 ProblemDetails responses:
/// <see cref="NotFoundException"/> → 404, <see cref="AppValidationException"/> → 400 (with an
/// <c>errors</c> dictionary), <see cref="ForbiddenException"/> → 403, <see cref="ConflictException"/>
/// → 409, <see cref="BusinessRuleException"/> → 422, anything else → 500 (details hidden outside
/// Development). Non-API requests are left to the regular error page.
/// </summary>
/// <remarks>
/// Expected application errors are normally converted earlier by <see cref="ApiErrorFilter"/>, so they
/// never reach the exception-handler middleware (which logs everything it sees at Error level in .NET 8).
/// This handler covers the rest: unexpected exceptions and errors raised outside endpoint filters.
/// </remarks>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService, IHostEnvironment environment, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!WebSetup.IsApiRequest(httpContext.Request))
        {
            return false;
        }

        var problem = CreateProblem(httpContext, exception, environment.IsDevelopment());
        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>The ProblemDetails (status, title, detail, validation errors) for an exception.</summary>
    internal static ProblemDetails CreateProblem(HttpContext httpContext, Exception exception, bool isDevelopment)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            AppValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            BusinessRuleException => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            BadHttpRequestException bad => (bad.StatusCode, "Bad request"),
            OperationCanceledException => (499, "Request cancelled"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        var problem = exception is AppValidationException validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        problem.Detail = exception is AppException or BadHttpRequestException || isDevelopment ? exception.Message : null;
        problem.Instance = httpContext.Request.Path;
        return problem;
    }
}

/// <summary>
/// Endpoint filter for the <c>/api/v1</c> group: turns expected application errors (<see cref="AppException"/>)
/// into ProblemDetails results directly, so routine 400/403/404/409/422 responses are not logged as server errors.
/// </summary>
internal sealed class ApiErrorFilter(IHostEnvironment environment) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (AppException ex)
        {
            return Results.Problem(ApiExceptionHandler.CreateProblem(context.HttpContext, ex, environment.IsDevelopment()));
        }
    }
}
