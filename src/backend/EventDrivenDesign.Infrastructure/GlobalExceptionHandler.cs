using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace EventDrivenDesign.Infrastructure;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problems,
    ILogger<GlobalExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path
        );

        bool malformed = exception is JsonException or BadHttpRequestException;
        var problem = new ProblemDetails
        {
            Status = malformed
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError,
            Title = malformed ? "Malformed request" : "Unexpected error",
        };
        if (!malformed)
        {
            problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        }

        httpContext.Response.StatusCode = problem.Status.Value;
        return await problems.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = exception,
            }
        );
    }
}
