using System.Text.Json;
using AwesomeAssertions;
using EventDrivenDesign.Infrastructure;
using EventDrivenDesign.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventDrivenDesign.Tests;

public class ResultTests
{
    private static readonly Error Boom = new("boom", "It broke.");

    [Fact]
    public void Success_has_no_error()
    {
        Result r = Result.Success();
        r.IsSuccess.Should().BeTrue();
        r.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_carries_its_error()
    {
        Result r = Result.Failure(Boom);
        r.IsSuccess.Should().BeFalse();
        r.Error.Should().Be(Boom);
        Boom.Kind.Should().Be(ErrorKind.Invalid);

        Result<int> t = Result<int>.Failure(Boom);
        t.IsSuccess.Should().BeFalse();
        t.Error.Should().Be(Boom);
    }

    [Fact]
    public void Value_of_a_failure_throws()
    {
        Result<int> r = Result<int>.Failure(Boom);
        r.Invoking(x => x.Value).Should().Throw<InvalidOperationException>();
        Result<int>.Success(7).Value.Should().Be(7);
    }

    [Fact]
    public void ToString_does_not_throw_on_a_failure()
    {
        Result<int>.Failure(Boom).Invoking(r => r.ToString()).Should().NotThrow();
        Result.Failure(Boom).Invoking(r => r.ToString()).Should().NotThrow();
    }

    [Fact]
    public void Match_takes_the_right_branch()
    {
        Result<int>.Success(7).Match(v => $"ok {v}", e => e.Code).Should().Be("ok 7");
        Result<int>.Failure(Boom).Match(v => $"ok {v}", e => e.Code).Should().Be("boom");
    }

    [Fact]
    public void Implicit_conversions()
    {
        Result fromError = Boom;
        fromError.IsSuccess.Should().BeFalse();
        fromError.Error.Should().Be(Boom);

        Result<int> fromValue = 5;
        fromValue.IsSuccess.Should().BeTrue();
        fromValue.Value.Should().Be(5);

        Result<int> failed = Boom;
        failed.IsSuccess.Should().BeFalse();
        failed.Error.Should().Be(Boom);
    }

    [Theory]
    [InlineData(ErrorKind.Invalid, 400)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Unavailable, 503)]
    public async Task Failed_result_maps_to_problem_by_kind(ErrorKind kind, int status)
    {
        await using ServiceProvider services = new ServiceCollection().AddProblemDetails().AddLogging().BuildServiceProvider();
        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.Response.Body = new MemoryStream();

        await Result.Failure(new Error("the-code", "msg", kind)).ToHttpResult().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(status);
        ctx.Response.Body.Position = 0;
        using JsonDocument body = await JsonDocument.ParseAsync(ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        body.RootElement.GetProperty("title").GetString().Should().Be("the-code");
    }
}
