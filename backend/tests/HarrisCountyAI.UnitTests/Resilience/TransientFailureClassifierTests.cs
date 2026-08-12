using System.Net.Sockets;
using Azure;
using HarrisCountyAI.Infrastructure.Resilience;
using HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

namespace HarrisCountyAI.UnitTests.Resilience;

public class TransientFailureClassifierTests
{
    [Theory]
    [InlineData(408)] // request timeout
    [InlineData(429)] // throttled
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void Server_Side_And_Throttling_Statuses_Are_Transient(int status)
    {
        Assert.True(TransientFailureClassifier.IsTransientStatus(status));
        Assert.True(TransientFailureClassifier.IsTransient(new RequestFailedException(status, "azure")));
        Assert.True(TransientFailureClassifier.IsTransient(FakeClientResultExceptions.WithStatus(status)));
    }

    [Theory]
    [InlineData(400)] // the request itself is wrong; it will be wrong next time too
    [InlineData(401)] // our credentials are wrong
    [InlineData(403)] // our credentials are insufficient
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(413)]
    [InlineData(422)]
    [InlineData(501)]
    public void Caller_And_Authentication_Statuses_Are_Not_Transient(int status)
    {
        Assert.False(TransientFailureClassifier.IsTransientStatus(status));
        Assert.False(TransientFailureClassifier.IsTransient(new RequestFailedException(status, "azure")));
        Assert.False(TransientFailureClassifier.IsTransient(FakeClientResultExceptions.WithStatus(status)));
    }

    [Fact]
    public void A_Missing_Response_Is_Transient()
    {
        // Status 0 is what the SDK reports when nothing came back at all.
        Assert.True(TransientFailureClassifier.IsTransientStatus(0));
    }

    [Fact]
    public void Transport_Failures_Are_Transient()
    {
        Assert.True(TransientFailureClassifier.IsTransient(new TimeoutException()));
        Assert.True(TransientFailureClassifier.IsTransient(new HttpRequestException("connection refused")));
        Assert.True(TransientFailureClassifier.IsTransient(new SocketException(10061)));
        Assert.True(TransientFailureClassifier.IsTransient(new IOException("stream closed")));
    }

    [Fact]
    public void Cancellation_Is_Not_Transient()
    {
        // The caller decided to stop. Repeating the call would defy them.
        Assert.False(TransientFailureClassifier.IsTransient(new OperationCanceledException()));
        Assert.False(TransientFailureClassifier.IsTransient(new TaskCanceledException()));
    }

    [Fact]
    public void Argument_Errors_Are_Not_Transient()
    {
        Assert.False(TransientFailureClassifier.IsTransient(new ArgumentException("bad input")));
        Assert.False(TransientFailureClassifier.IsTransient(new ArgumentNullException("value")));
    }

    [Fact]
    public void Null_Is_Not_Transient()
    {
        Assert.False(TransientFailureClassifier.IsTransient(null));
    }

    [Fact]
    public void A_Wrapped_Transient_Failure_Is_Transient()
    {
        var wrapped = new InvalidOperationException(
            "indexing failed", new RequestFailedException(503, "service unavailable"));

        Assert.True(TransientFailureClassifier.IsTransient(wrapped));
    }

    [Fact]
    public void A_Wrapped_Permanent_Failure_Is_Not_Transient()
    {
        var wrapped = new InvalidOperationException(
            "indexing failed", new RequestFailedException(400, "bad request"));

        Assert.False(TransientFailureClassifier.IsTransient(wrapped));
    }

    [Fact]
    public void An_Aggregate_Is_Transient_When_Any_Inner_Failure_Is()
    {
        var aggregate = new AggregateException(
            new ArgumentException("bad input"),
            new RequestFailedException(429, "throttled"));

        Assert.True(TransientFailureClassifier.IsTransient(aggregate));
    }

    [Theory]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(400, false)]
    [InlineData(503, false)]
    public void Authentication_Failures_Are_Identified(int status, bool expected)
    {
        Assert.Equal(expected, TransientFailureClassifier.IsAuthenticationFailure(status));
    }

    [Fact]
    public void Status_Is_Read_From_Azure_And_Client_Exceptions_Including_Nested_Ones()
    {
        Assert.Equal(503, TransientFailureClassifier.GetStatus(new RequestFailedException(503, "down")));
        Assert.Equal(429, TransientFailureClassifier.GetStatus(FakeClientResultExceptions.WithStatus(429)));
        Assert.Equal(
            404,
            TransientFailureClassifier.GetStatus(
                new InvalidOperationException("wrapped", new RequestFailedException(404, "missing"))));
        Assert.Null(TransientFailureClassifier.GetStatus(new TimeoutException()));
        Assert.Null(TransientFailureClassifier.GetStatus(null));
    }
}
