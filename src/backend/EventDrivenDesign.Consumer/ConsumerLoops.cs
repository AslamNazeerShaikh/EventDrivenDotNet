using Confluent.Kafka;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Persistence;

namespace EventDrivenDesign.Consumer;

public static class ConsumerLoops
{
    /// <summary>Registers the readings and alarms loops (doc 00 §7). Plain AddSingleton, not AddHostedService: that dedupes by implementation type, so the second LoopService would be dropped.</summary>
    public static IServiceCollection AddStoreWriterLoops(this IServiceCollection services, string bootstrap, string consumerId, int maxRowsPerSecond)
    {
        IConsumer<string, byte[]> Build(string group, ILogger log) =>
            new ConsumerBuilder<string, byte[]>(KafkaConfigs.StoreWriter(bootstrap, group))
                .SetPartitionsRevokedHandler((c, _) => BatchLoop<object>.CommitOnRevoke(c, log))
                .Build();

        services.AddSingleton(sp =>
        {
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("readings");
            var store = sp.GetRequiredService<CentralStore>();
            return new BatchLoop<MachineReading>(Build("central-readings", log), TopicNames.CentralReadingsPattern, 5000, TimeSpan.FromMilliseconds(200),
                new RateCap(maxRowsPerSecond), (batch, rejected, ct) => store.InsertReadingsAsync(batch, rejected, consumerId, ct),
                sp.GetRequiredService<ConsumerStats>(), log);
        });
        services.AddSingleton<IHostedService>(sp => new LoopService(sp.GetRequiredService<BatchLoop<MachineReading>>().Run));

        services.AddSingleton(sp =>
        {
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("alarms");
            var store = sp.GetRequiredService<CentralStore>();
            return new BatchLoop<MachineAlarm>(Build("central-alarms", log), TopicNames.CentralAlarmsPattern, 100, TimeSpan.FromMilliseconds(50), null,
                (batch, rejected, ct) => store.InsertAlarmsAsync(batch, rejected, consumerId, ct), sp.GetRequiredService<ConsumerStats>(), log);
        });
        services.AddSingleton<IHostedService>(sp => new LoopService(sp.GetRequiredService<BatchLoop<MachineAlarm>>().Run));

        return services;
    }
}
