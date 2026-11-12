using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace EventDrivenDesign.Infrastructure;

public static class HostingExtensions
{
    public static IServiceCollection AddGlobalExceptionHandling(this IServiceCollection services) =>
        services.AddProblemDetails().AddExceptionHandler<GlobalExceptionHandler>();

    public static WebApplication UseGlobalExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}
