using System.Net;
using System.Net.Http.Headers;
using HarrisCountyAI.Api.Middleware;
using Microsoft.AspNetCore.Hosting;

namespace HarrisCountyAI.IntegrationTests.Api;

/// <summary>
/// Covers the local-development CORS policy: the Angular dev server on
/// :4200 has to reach the API on :5096, and nothing else should.
/// </summary>
public class CorsTests : IClassFixture<TestApplicationFactory>
{
    private const string AllowedOrigin = "http://localhost:4200";
    private const string AllowOriginHeader = "Access-Control-Allow-Origin";
    private const string ExposeHeadersHeader = "Access-Control-Expose-Headers";

    private readonly TestApplicationFactory _factory;

    public CorsTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Preflight_From_Dev_Server_Origin_Is_Allowed()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/cases");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues(AllowOriginHeader)));
    }

    [Fact]
    public async Task Response_Exposes_Correlation_Id_To_The_Browser()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues(AllowOriginHeader)));
        Assert.Contains(
            CorrelationIdMiddleware.HeaderName,
            response.Headers.GetValues(ExposeHeadersHeader));
    }

    [Fact]
    public async Task Unlisted_Origin_Is_Not_Allowed()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://not-the-dev-server.example");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains(AllowOriginHeader));
    }

    [Fact]
    public async Task Policy_Is_Not_Registered_Outside_Development()
    {
        using var factory = new ProductionApplicationFactory();
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains(AllowOriginHeader));
    }

    /// <summary>
    /// The shared factory pins the environment to Development; this one flips it
    /// back to Production so the environment gate itself can be asserted.
    /// </summary>
    private sealed class ProductionApplicationFactory : TestApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }
}
