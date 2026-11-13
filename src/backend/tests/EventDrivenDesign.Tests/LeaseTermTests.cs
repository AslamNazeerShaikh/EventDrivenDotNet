using AwesomeAssertions;
using EventDrivenDesign.Gateway;
using Xunit;

namespace EventDrivenDesign.Tests;

public class LeaseTermTests
{
    [Fact]
    public void Lost_then_assigned_starts_a_new_term()
    {
        var term = new LeaseTerm();
        term.Active.Should().BeFalse();
        term.WasLost.Should().BeFalse();

        term.Assigned(true).Should().Be(LeaseDecision.StartTerm);
        term.Active.Should().BeTrue();
        term.Assigned(true).Should().Be(LeaseDecision.None);

        term.Lost().Should().Be(LeaseDecision.FlushSender);
        term.WasLost.Should().BeTrue();
        term.Active.Should().BeTrue(); // unchanged by a lost session

        term.Assigned(true).Should().Be(LeaseDecision.StartNewTerm);
        term.WasLost.Should().BeFalse();
        term.Active.Should().BeTrue();

        term.Revoked(true).Should().Be(LeaseDecision.StopTerm);
        term.Active.Should().BeFalse();

        // While inactive nothing fires.
        term.Assigned(false).Should().Be(LeaseDecision.None);
        term.Revoked(false).Should().Be(LeaseDecision.None);
        term.Lost().Should().Be(LeaseDecision.None);
    }

    [Fact]
    public void Assigned_without_the_lease_stops_the_term()
    {
        var term = new LeaseTerm();
        term.Assigned(true).Should().Be(LeaseDecision.StartTerm);
        term.Assigned(false).Should().Be(LeaseDecision.StopTerm);
        term.Active.Should().BeFalse();

        // A revoke with no term running is not a stop either.
        term.Revoked(true).Should().Be(LeaseDecision.None);
    }
}
