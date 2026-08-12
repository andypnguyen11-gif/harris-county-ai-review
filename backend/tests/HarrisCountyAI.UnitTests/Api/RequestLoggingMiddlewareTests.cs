using HarrisCountyAI.Api.Middleware;
using HarrisCountyAI.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HarrisCountyAI.UnitTests.Api;

public class RequestLoggingMiddlewareTests
{
    [Fact]
    public async Task Logs_Method_Path_Status_And_Duration()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/cases";

        var middleware = new RequestLoggingMiddleware(
            innerContext =>
            {
                innerContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        var stateValues = entry.StateValues.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal("GET", stateValues["RequestMethod"]);
        Assert.Equal("/api/cases", stateValues["RequestPath"]);
        Assert.Equal(404, stateValues["StatusCode"]);
        Assert.True(stateValues.ContainsKey("ElapsedMilliseconds"));
    }

    [Fact]
    public async Task Logs_Error_And_Rethrows_When_Next_Throws()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/documents";
        var failure = new InvalidOperationException("boom");

        var middleware = new RequestLoggingMiddleware(_ => throw failure, logger);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(failure, thrown);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Exception);
        var stateValues = entry.StateValues.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal("POST", stateValues["RequestMethod"]);
        Assert.Equal("/api/documents", stateValues["RequestPath"]);
    }
}
