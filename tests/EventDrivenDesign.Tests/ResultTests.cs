using EventDrivenDesign.Infrastructure;
using EventDrivenDesign.Messaging;
using EventDrivenDesign.Shared;
using Xunit;

namespace EventDrivenDesign.Tests;

/// <summary>
/// Verifies Result semantics and the small pure-logic helpers the data plane relies on.
/// (Rate maths and CPU formula live here until Task 2 extracts dedicated test files.)
/// </summary>
public sealed class ResultTests
{
    /// <summary>
    /// Verifies an Ok result carries its value and reports success.
    /// </summary>
    [Fact]
    public void Ok_CarriesValue()
    {
        Result<int> result = Result<int>.Ok(42);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    /// <summary>
    /// Verifies a Fail result carries its error and reports failure.
    /// </summary>
    [Fact]
    public void Fail_CarriesError()
    {
        Result<int> result = Result<int>.Fail("boom");
        Assert.False(result.IsSuccess);
        Assert.Equal("boom", result.Error);
        Assert.Equal(default, result.Value);
    }

    /// <summary>
    /// Verifies empty error messages are rejected.
    /// </summary>
    /// <param name="error">The invalid error message.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Fail_RejectsEmptyError(string error)
    {
        Assert.Throws<ArgumentException>(() => Result<int>.Fail(error));
    }

    /// <summary>
    /// Verifies per-tick counts sum to the commanded rate over one second.
    /// </summary>
    /// <param name="ratePerSecond">The commanded rate.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(200)]
    [InlineData(205)]
    public void RateMath_TicksSumToRate(int ratePerSecond)
    {
        int total = 0;
        for (int tick = 0; tick < RateMath.TicksPerSecond; tick++)
        {
            total += RateMath.EventsForTick(ratePerSecond, tick);
        }

        Assert.Equal(ratePerSecond, total);
    }

    /// <summary>
    /// Verifies the remainder is spread one extra event per tick across the first ticks.
    /// </summary>
    [Fact]
    public void RateMath_DistributesRemainder()
    {
        Assert.Equal(21, RateMath.EventsForTick(205, 0));
        Assert.Equal(21, RateMath.EventsForTick(205, 4));
        Assert.Equal(20, RateMath.EventsForTick(205, 5));
        Assert.Equal(20, RateMath.EventsForTick(205, 9));
    }

    /// <summary>
    /// Verifies invalid rate inputs are rejected.
    /// </summary>
    [Fact]
    public void RateMath_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RateMath.EventsForTick(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RateMath.EventsForTick(100, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RateMath.EventsForTick(100, RateMath.TicksPerSecond));
    }

    /// <summary>
    /// Verifies the CPU percentage formula from spec section 8.
    /// </summary>
    [Fact]
    public void CpuPercent_MatchesSpecFormula()
    {
        double cpu = ResourceSampler.ComputeCpuPercent(
            TimeSpan.Zero, TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(100), 4);
        Assert.Equal(50.0, cpu);
    }

    /// <summary>
    /// Verifies zero wall time yields zero instead of dividing by zero.
    /// </summary>
    [Fact]
    public void CpuPercent_ZeroWallTime_ReturnsZero()
    {
        Assert.Equal(0.0, ResourceSampler.ComputeCpuPercent(
            TimeSpan.Zero, TimeSpan.FromMilliseconds(200), TimeSpan.Zero, 4));
    }
}
