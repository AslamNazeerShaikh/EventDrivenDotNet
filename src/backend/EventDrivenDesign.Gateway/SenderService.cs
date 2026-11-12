using Microsoft.Extensions.Hosting;

namespace EventDrivenDesign.Gateway;

/// <summary>Runs the sender loop for the host's lifetime.</summary>
public sealed class SenderService(Sender sender) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => sender.RunAsync(stoppingToken);
}
