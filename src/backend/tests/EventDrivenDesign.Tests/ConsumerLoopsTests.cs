using AwesomeAssertions;
using EventDrivenDesign.Consumer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EventDrivenDesign.Tests;

public sealed class ConsumerLoopsTests
{
    [Fact]
    public void Readings_and_alarms_loops_are_both_registered_as_hosted_services()
    {
        var services = new ServiceCollection();

        services.AddStoreWriterLoops("localhost:9092", "c1", 1000);

        // AddHostedService dedupes by implementation type, so two LoopService registrations would silently leave one.
        services.Count(d => d.ServiceType == typeof(IHostedService)).Should().Be(2);
    }
}
