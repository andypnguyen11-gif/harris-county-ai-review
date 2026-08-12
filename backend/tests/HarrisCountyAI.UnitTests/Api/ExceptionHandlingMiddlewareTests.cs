using System.Text.Json;
using HarrisCountyAI.Api.Errors;
using HarrisCountyAI.Api.Middleware;
using HarrisCountyAI.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Api;

public class ExceptionHandlingMiddlewareTests
{
    private const string CorrelationId = "test-correlation-id-1234";

    private static HttpContext CreateContext(string path = "/api/questions")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        // Mirrors what the correlation-id middleware puts there.
        context.Items[ApiProblemDetails.CorrelationIdItemKey] = CorrelationId;
        return context;
    }

    private static ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next) =>
        new(
            next,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            new ApiProblemDetailsFactory(Options.Create(new ApiBehaviorOptions())));

    private static ExceptionHandlingMiddleware CreateMiddlewareThatThrows(Exception exception) =>
        CreateMiddleware(_ => throw exception);

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task A_Successful_Request_Is_Left_Alone()
    {
        var context = CreateContext();
        var middleware = CreateMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task A_Search_Outage_Becomes_A_503_Problem_Document()
    {
        var context = CreateContext();
        var middleware = CreateMiddlewareThatThrows(new ExternalServiceUnavailableException(
            ExternalServiceNames.Search,
            "https://harriscounty-search.search.windows.net returned 503",
            statusCode: 503));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("30", context.Response.Headers.RetryAfter.ToString());

        var body = await ReadBodyAsync(context);
        Assert.Equal(503, body.GetProperty("status").GetInt32());
        Assert.Equal(ExternalServiceNames.Search, body.GetProperty("service").GetString());
        Assert.Equal(CorrelationId, body.GetProperty("correlationId").GetString());
        Assert.Contains("temporarily unavailable", body.GetProperty("title").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Document_Intelligence_Timeout_Becomes_A_504()
    {
        var context = CreateContext("/api/cases/1/documents");
        var middleware = CreateMiddlewareThatThrows(new ExternalServiceTimeoutException(
            ExternalServiceNames.DocumentIntelligence, "analysis timed out after 120s"));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
        var body = await ReadBodyAsync(context);
        Assert.Equal(ExternalServiceNames.DocumentIntelligence, body.GetProperty("service").GetString());
        Assert.Equal("10", context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Unusable_Model_Output_Becomes_A_502()
    {
        var context = CreateContext();
        var middleware = CreateMiddlewareThatThrows(
            new MalformedModelResponseException("deployment gpt-5-mini-prod returned no content"));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        var body = await ReadBodyAsync(context);
        Assert.Equal(ExternalServiceNames.LanguageModel, body.GetProperty("service").GetString());
        Assert.False(context.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task An_Unexpected_Failure_Becomes_A_500_That_Blames_No_Dependency()
    {
        var context = CreateContext();
        var middleware = CreateMiddlewareThatThrows(new InvalidOperationException("mapper bug"));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var body = await ReadBodyAsync(context);
        Assert.False(body.TryGetProperty("service", out _));
        Assert.Equal(CorrelationId, body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task No_Exception_Detail_Reaches_The_Response_Body()
    {
        var context = CreateContext();
        var middleware = CreateMiddlewareThatThrows(new ExternalServiceUnavailableException(
            ExternalServiceNames.DocumentStorage,
            "DefaultEndpointsProtocol=https;AccountName=hcaistorage;AccountKey=SUPERSECRETKEY==",
            new IOException("blob case-documents/abc.pdf could not be read")));

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var raw = await reader.ReadToEndAsync();

        Assert.DoesNotContain("SUPERSECRETKEY", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hcaistorage", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("case-documents/abc.pdf", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExceptionHandlingMiddleware", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("at Harris", raw, StringComparison.Ordinal);

        // What it does carry is the handle into the logs.
        Assert.Contains(CorrelationId, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Client_Who_Walked_Away_Gets_No_Response_Body()
    {
        var context = CreateContext();
        context.RequestAborted = new CancellationToken(canceled: true);
        var middleware = CreateMiddlewareThatThrows(new OperationCanceledException());

        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task A_Cancellation_That_Is_Not_The_Clients_Is_Still_Reported()
    {
        var context = CreateContext();
        var middleware = CreateMiddlewareThatThrows(new OperationCanceledException());

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_Response_Already_On_The_Wire_Is_Not_Rewritten()
    {
        // Streaming a document that fails halfway cannot become a problem
        // document; a truncated body must not be passed off as a whole one.
        var aborted = false;
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            Method = "GET",
            Path = "/api/cases/1/documents/2/content",
        });
        features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        features.Set<IHttpRequestLifetimeFeature>(new AbortTrackingLifetimeFeature(() => aborted = true));

        var context = new DefaultHttpContext(features);
        context.Items[ApiProblemDetails.CorrelationIdItemKey] = CorrelationId;

        var middleware = CreateMiddlewareThatThrows(new IOException("stream broke"));

        await middleware.InvokeAsync(context);

        Assert.True(aborted);
    }

    [Fact]
    public async Task Without_A_Correlation_Id_The_Trace_Identifier_Is_Used()
    {
        var context = CreateContext();
        context.Items.Remove(ApiProblemDetails.CorrelationIdItemKey);
        context.TraceIdentifier = "trace-fallback-id";

        var middleware = CreateMiddlewareThatThrows(new InvalidOperationException("boom"));

        await middleware.InvokeAsync(context);

        var body = await ReadBodyAsync(context);
        Assert.Equal("trace-fallback-id", body.GetProperty("correlationId").GetString());
    }

    /// <summary>Response feature that reports headers as already sent.</summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = new MemoryStream();

        public bool HasStarted => true;

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public string? ReasonPhrase { get; set; }

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }

    /// <summary>Lifetime feature that records whether the connection was aborted.</summary>
    private sealed class AbortTrackingLifetimeFeature(Action onAbort) : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }

        public void Abort() => onAbort();
    }
}
