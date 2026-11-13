using AwesomeAssertions;
using EventDrivenDesign.Infrastructure;
using Xunit;

namespace EventDrivenDesign.Tests;

public class ProcessSamplerTests
{
    [Fact]
    public void Process_sampler_reports_plausible_numbers()
    {
        var s = new ProcessSampler();
        var p = s.Sample();
        p.CpuPercent.Should().BeGreaterThanOrEqualTo(0);
        p.WorkingSetBytes.Should().BeGreaterThan(0);
        p.GcHeapBytes.Should().BeGreaterThan(0);
    }
}
