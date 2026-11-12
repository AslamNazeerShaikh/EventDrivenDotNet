using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EventDrivenDesign.Contracts;
using EventDrivenDesign.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EventDrivenDesign.Tests;

public class GlobalExceptionHandlerTests
{
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        Action<WebApplication> map
    )
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddGlobalExceptionHandling();
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, Json.Options.TypeInfoResolver!)
        );
        WebApplication app = builder.Build();
        app.UseGlobalExceptionHandling();
        map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        string address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(address) });
    }

    [Fact]
    public async Task Unhandled_exception_becomes_problem_500_without_leaking()
    {
        (WebApplication app, HttpClient client) = await StartAsync(a => a.MapGet("/boom", () =>
            {
                throw new InvalidOperationException("secret detail");
            }));
        await using (app)
        using (client)
        {
            HttpResponseMessage res = await client.GetAsync(
                "/boom",
                TestContext.Current.CancellationToken
            );
            string body = await res.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken
            );

            ((int)res.StatusCode).Should().Be(500);
            res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            body.Should().Contain("traceId");
            body.Should().NotContain("secret detail");
        }
    }

    [Fact]
    public async Task Malformed_json_becomes_400()
    {
        (WebApplication app, HttpClient client) = await StartAsync(a =>
            a.MapPost("/cmd", (GatewayCommand _) => Results.Accepted())
        );
        await using (app)
        using (client)
        {
            HttpResponseMessage res = await client.PostAsync(
                "/cmd",
                new StringContent("[]", Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken
            );

            ((int)res.StatusCode).Should().Be(400);
            res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("5")]
    public void Converter_rejects_a_non_object_body_with_JsonException(string body)
    {
        Action act = () => JsonSerializer.Deserialize<GatewayCommand>(body, Json.Options);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task Json_exception_from_an_endpoint_becomes_malformed_request_400()
    {
        (WebApplication app, HttpClient client) = await StartAsync(a => a.MapGet("/bad", () =>
            {
                throw new JsonException("x");
            }));
        await AssertMalformedAsync(app, client);
    }

    [Fact]
    public async Task Bad_http_request_from_an_endpoint_becomes_malformed_request_400()
    {
        (WebApplication app, HttpClient client) = await StartAsync(a => a.MapGet("/bad", () =>
            {
                throw new BadHttpRequestException("x");
            }));
        await AssertMalformedAsync(app, client);
    }

    private static async Task AssertMalformedAsync(WebApplication app, HttpClient client)
    {
        await using (app)
        using (client)
        {
            HttpResponseMessage res = await client.GetAsync(
                "/bad",
                TestContext.Current.CancellationToken
            );
            string body = await res.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken
            );

            ((int)res.StatusCode).Should().Be(400);
            res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            JsonDocument
                .Parse(body)
                .RootElement.GetProperty("title")
                .GetString()
                .Should()
                .Be("Malformed request");
        }
    }
}
