using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EventDrivenDesign.Shared;

/// <summary>
/// Global exception handler: turns unhandled exceptions into RFC 7807 problem details.
/// Register once per host with <c>app.UseMiddleware&lt;ExceptionMiddleware&gt;()</c>.
/// </summary>
public sealed partial class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _log;

    /// <summary>
    /// Creates the middleware.
    /// </summary>
    /// <param name="next">The next delegate in the pipeline.</param>
    /// <param name="log">The logger.</param>
    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    /// <summary>
    /// Runs the pipeline, mapping any exception to a 500 problem-details response.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the request is handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            LogUnhandled(_log, ex);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Unhandled error.",
                status = 500,
            }, context.RequestAborted);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception processing request.")]
    private static partial void LogUnhandled(ILogger log, Exception ex);
}
