namespace EventDrivenDesign.Consumer;

/// <summary>Runs a blocking loop on its own thread for the host's lifetime; an unexpected exception stops the host so Compose restarts it.</summary>
public sealed class LoopService(Action<CancellationToken> run) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => run(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
