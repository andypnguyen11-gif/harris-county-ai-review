using Azure;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Infrastructure.Resilience;
using HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

namespace HarrisCountyAI.UnitTests.Resilience;

public class AzureOperationExecutorTests
{
    private const string Service = ExternalServiceNames.Search;

    private static Task<T> Execute<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        => AzureOperationExecutor.ExecuteAsync(Service, "query", action, cancellationToken);

    [Fact]
    public async Task A_Successful_Call_Passes_Its_Result_Through()
    {
        var result = await Execute(_ => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task The_Caller_Token_Reaches_The_Operation()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken observed = default;

        await Execute(token =>
        {
            observed = token;
            return Task.FromResult(0);
        }, cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    public async Task A_Server_Side_Failure_Becomes_An_Unavailable_Exception(int status)
    {
        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => Execute<int>(_ => throw new RequestFailedException(status, "azure said no")));

        Assert.Equal(Service, exception.ServiceName);
        Assert.Equal(status, exception.StatusCode);
        Assert.IsType<RequestFailedException>(exception.InnerException);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task A_Rejected_Credential_Is_Our_Outage_Not_The_Callers_Problem(int status)
    {
        // The reviewer's own authentication is fine; ours to Azure is not.
        // Surfacing this as a 401 to the caller would send them to re-sign-in
        // for a problem only an operator can fix, so it stays a dependency
        // failure.
        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => Execute<int>(_ => throw new RequestFailedException(status, "forbidden")));

        Assert.Equal(status, exception.StatusCode);
    }

    [Fact]
    public async Task A_Client_Result_Failure_Becomes_An_Unavailable_Exception()
    {
        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => Execute<int>(_ => throw FakeClientResultExceptions.WithStatus(502)));

        Assert.Equal(502, exception.StatusCode);
    }

    [Fact]
    public async Task A_Transport_Failure_Becomes_An_Unavailable_Exception()
    {
        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => Execute<int>(_ => throw new HttpRequestException("connection refused")));

        Assert.Equal(Service, exception.ServiceName);
        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task A_Timeout_Becomes_A_Timeout_Exception()
    {
        var exception = await Assert.ThrowsAsync<ExternalServiceTimeoutException>(
            () => Execute<int>(_ => throw new TimeoutException("too slow")));

        Assert.Equal(Service, exception.ServiceName);
        Assert.IsAssignableFrom<TimeoutException>(exception);
    }

    [Fact]
    public async Task A_408_Becomes_A_Timeout_Exception_Rather_Than_An_Outage()
    {
        var exception = await Assert.ThrowsAsync<ExternalServiceTimeoutException>(
            () => Execute<int>(_ => throw new RequestFailedException(408, "request timeout")));

        Assert.Equal(Service, exception.ServiceName);
    }

    [Fact]
    public async Task Cancellation_That_Is_Not_The_Callers_Becomes_A_Timeout_Exception()
    {
        // An internal budget expired: the caller is still waiting, so they get
        // a timeout rather than a cancellation they never asked for.
        await Assert.ThrowsAsync<ExternalServiceTimeoutException>(
            () => Execute<int>(_ => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task The_Callers_Own_Cancellation_Is_Passed_Through_Untouched()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Execute<int>(_ => throw new OperationCanceledException(cts.Token), cts.Token));

        Assert.IsNotType<ExternalServiceTimeoutException>(exception);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(412)]
    public async Task Not_Found_And_Conflict_Stay_Themselves(int status)
    {
        // "It is not there" and "someone changed it" are answers the caller
        // acts on, not outages.
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => Execute<int>(_ => throw new RequestFailedException(status, "no")));

        Assert.Equal(status, exception.Status);
    }

    [Fact]
    public async Task A_Missing_File_Is_Passed_Through_So_Callers_Can_Report_A_404()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => Execute<int>(_ => throw new FileNotFoundException("gone", "case/doc.pdf")));
    }

    [Fact]
    public async Task Our_Own_Bugs_Are_Not_Disguised_As_Dependency_Outages()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Execute<int>(_ => throw new ArgumentException("bad filter")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Execute<int>(_ => throw new InvalidOperationException("mapping bug")));
    }

    [Fact]
    public async Task An_Already_Translated_Failure_Is_Not_Translated_Twice()
    {
        var original = new ExternalServiceUnavailableException(
            ExternalServiceNames.Embeddings, "embeddings are down");

        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => Execute<int>(_ => throw original));

        Assert.Same(original, exception);
    }

    [Fact]
    public async Task The_Void_Overload_Translates_The_Same_Way()
    {
        var exception = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => AzureOperationExecutor.ExecuteAsync(
                ExternalServiceNames.DocumentStorage,
                "upload",
                _ => throw new RequestFailedException(503, "down"),
                CancellationToken.None));

        Assert.Equal(ExternalServiceNames.DocumentStorage, exception.ServiceName);
    }

    [Fact]
    public void The_Translated_Message_Names_The_Capability_And_Nothing_Else()
    {
        // The SDK message carries the endpoint. It belongs in the log (as the
        // inner exception) and must not be repeated in the message the API
        // layer might otherwise be tempted to surface.
        var sdkFailure = new RequestFailedException(
            503, "Service request failed. Status: 503. https://harriscounty-search.search.windows.net/indexes('x')");

        var translated = AzureOperationExecutor.Translate(
            ExternalServiceNames.Search, "query", sdkFailure, CancellationToken.None);

        Assert.DoesNotContain("search.windows.net", translated.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ExternalServiceNames.Search, translated.Message, StringComparison.Ordinal);
        Assert.Same(sdkFailure, translated.InnerException);
    }
}
