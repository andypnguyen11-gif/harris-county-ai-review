using System.Net;
using HarrisCountyAI.Api.Middleware;

namespace HarrisCountyAI.IntegrationTests.Api;

public class CorrelationIdTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public CorrelationIdTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Responses_Carry_A_Generated_Correlation_Id()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        var correlationId = Assert.Single(values);
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
    }

    [Fact]
    public async Task Responses_Echo_The_Incoming_Correlation_Id()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "integration-test-correlation-id");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        Assert.Equal("integration-test-correlation-id", Assert.Single(values));
    }

    [Fact]
    public async Task Overlong_Incoming_Correlation_Ids_Are_Replaced()
    {
        var client = _factory.CreateClient();
        var overlongId = new string('a', 65);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, overlongId);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        var correlationId = Assert.Single(values);
        Assert.NotEqual(overlongId, correlationId);
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
    }

    [Fact]
    public async Task Error_Responses_Also_Carry_The_Correlation_Id()
    {
        var client = _factory.CreateClient().WithToken(TestAuthentication.CreateToken(roles: ["Reviewer"]));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/this-route-does-not-exist");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "not-found-correlation-id");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        Assert.Equal("not-found-correlation-id", Assert.Single(values));
    }

    [Fact]
    public async Task Rejected_Requests_Still_Carry_The_Correlation_Id()
    {
        // Authorization short-circuits the pipeline before routing, so the correlation id is only
        // present if it is assigned upstream of the authorization middleware. A rejected request is
        // exactly the case a reviewer is most likely to report, so it has to be traceable.
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/cases");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "unauthenticated-correlation-id");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        Assert.Equal("unauthenticated-correlation-id", Assert.Single(values));
    }

    [Fact]
    public async Task Forbidden_Requests_Still_Carry_The_Correlation_Id()
    {
        var client = _factory.CreateClient().WithToken(TestAuthentication.CreateToken(roles: ["Reviewer"]));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/knowledge-base/documents");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "forbidden-correlation-id");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        Assert.Equal("forbidden-correlation-id", Assert.Single(values));
    }
}
