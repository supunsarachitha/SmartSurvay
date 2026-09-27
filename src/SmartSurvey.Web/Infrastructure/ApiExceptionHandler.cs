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
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService, IHostEnvironment environment, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!WebSetup.IsApiRequest(httpContext.Request))
        {
            return false;
        }

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

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled API exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        var problem = exception is AppValidationException validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        problem.Detail = exception is AppException or BadHttpRequestException || environment.IsDevelopment()
            ? exception.Message
            : null;
        problem.Instance = httpContext.Request.Path;

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
