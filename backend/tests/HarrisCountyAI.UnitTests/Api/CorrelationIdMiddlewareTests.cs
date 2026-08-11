using HarrisCountyAI.Api.Middleware;
using HarrisCountyAI.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace HarrisCountyAI.UnitTests.Api;

public class CorrelationIdMiddlewareTests
{
    private static CorrelationIdMiddleware CreateMiddleware(
        CapturingLogger<CorrelationIdMiddleware> logger,
        RequestDelegate? next = null) =>
        new(next ?? (_ => Task.CompletedTask), logger);

    [Fact]
    public async Task Uses_Incoming_Header_When_Present()
    {
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "client-supplied-id-123";

        await CreateMiddleware(logger).InvokeAsync(context);

        Assert.Equal("client-supplied-id-123", context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
        Assert.Equal("client-supplied-id-123", context.Items[CorrelationIdMiddleware.ItemKey]);
    }

    [Fact]
    public async Task Generates_Id_When_Header_Missing()
    {
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        var context = new DefaultHttpContext();

        await CreateMiddleware(logger).InvokeAsync(context);

        var correlationId = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
        Assert.Equal(correlationId, context.Items[CorrelationIdMiddleware.ItemKey]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("id with spaces")]
    [InlineData("bad\"quote")]
    public async Task Replaces_Unusable_Incoming_Values(string incoming)
    {
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incoming;

        await CreateMiddleware(logger).InvokeAsync(context);

        var correlationId = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.NotEqual(incoming, correlationId);
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
    }

    [Fact]
    public async Task Replaces_Overlong_Incoming_Values()
    {
        var incoming = new string('a', 65);
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incoming;

        await CreateMiddleware(logger).InvokeAsync(context);

        var correlationId = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.NotEqual(incoming, correlationId);
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
    }

    [Fact]
    public async Task Pushes_Correlation_Id_Onto_Logging_Scope_Around_Next()
    {
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        var scopesWhenNextRan = 0;
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "scope-test-id";

        var middleware = CreateMiddleware(logger, _ =>
        {
            scopesWhenNextRan = logger.Scopes.Count;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(1, scopesWhenNextRan);
        var scope = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(
            Assert.Single(logger.Scopes));
        Assert.Equal("scope-test-id", scope[CorrelationIdMiddleware.ItemKey]);
    }

    [Fact]
    public async Task Sets_Response_Header_Before_Next_Runs()
    {
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        string? headerDuringNext = null;
        var context = new DefaultHttpContext();

        var middleware = CreateMiddleware(logger, innerContext =>
        {
            headerDuringNext = innerContext.Response.Headers[CorrelationIdMiddleware.HeaderName];
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(string.IsNullOrEmpty(headerDuringNext));
    }
}
