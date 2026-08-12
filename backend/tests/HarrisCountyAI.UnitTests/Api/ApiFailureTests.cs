using HarrisCountyAI.Api.Errors;
using HarrisCountyAI.Application.Common.Exceptions;
using HarrisCountyAI.Infrastructure.Azure.LanguageModels;
using Microsoft.AspNetCore.Http;

namespace HarrisCountyAI.UnitTests.Api;

public class ApiFailureTests
{
    [Fact]
    public void A_Dependency_Outage_Is_A_503_Naming_The_Capability()
    {
        var failure = ApiFailure.From(new ExternalServiceUnavailableException(
            ExternalServiceNames.Search, "search endpoint returned 503", statusCode: 503));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, failure.StatusCode);
        Assert.Equal(ExternalServiceNames.Search, failure.ServiceName);
        Assert.Contains("Search", failure.Detail, StringComparison.Ordinal);
        Assert.Equal(30, failure.RetryAfterSeconds);
    }

    [Fact]
    public void A_Dependency_Timeout_Is_A_504()
    {
        var failure = ApiFailure.From(new ExternalServiceTimeoutException(
            ExternalServiceNames.DocumentIntelligence, "analysis timed out"));

        Assert.Equal(StatusCodes.Status504GatewayTimeout, failure.StatusCode);
        Assert.Equal(ExternalServiceNames.DocumentIntelligence, failure.ServiceName);
        Assert.Equal(10, failure.RetryAfterSeconds);
    }

    [Fact]
    public void A_Plain_Timeout_Is_Still_A_504_Even_Without_A_Service_Name()
    {
        var failure = ApiFailure.From(new TimeoutException("something took too long"));

        Assert.Equal(StatusCodes.Status504GatewayTimeout, failure.StatusCode);
        Assert.Null(failure.ServiceName);
    }

    [Fact]
    public void Unusable_Model_Output_Is_A_502_And_Is_Not_Presented_As_Retryable()
    {
        var failure = ApiFailure.From(
            new MalformedModelResponseException("the model returned no content"));

        Assert.Equal(StatusCodes.Status502BadGateway, failure.StatusCode);
        Assert.Equal(ExternalServiceNames.LanguageModel, failure.ServiceName);
        Assert.Null(failure.RetryAfterSeconds);
    }

    [Fact]
    public void An_Embedding_Outage_Is_Classified_Like_Any_Other_Dependency()
    {
        var failure = ApiFailure.From(new EmbeddingServiceException("all attempts failed"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, failure.StatusCode);
        Assert.Equal(ExternalServiceNames.Embeddings, failure.ServiceName);
    }

    [Fact]
    public void Anything_Else_Is_A_500_That_Blames_Nobody()
    {
        var failure = ApiFailure.From(new InvalidOperationException("null reference in the mapper"));

        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);
        Assert.Null(failure.ServiceName);
        Assert.Contains("correlation id", failure.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(LeakyExceptions))]
    public void No_Exception_Text_Ever_Reaches_The_Detail(Exception exception)
    {
        var failure = ApiFailure.From(exception);

        foreach (var secret in Secrets)
        {
            Assert.DoesNotContain(secret, failure.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, failure.Title, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Fragments that must never appear in a response body.</summary>
    private static readonly string[] Secrets =
    [
        "search.windows.net",
        "harriscounty-ai-storage",
        "AccountKey",
        "sv=2025-11-05&sig=",
        "gpt-5-mini-prod",
        "cognitiveservices.azure.com",
    ];

    public static TheoryData<Exception> LeakyExceptions() =>
    [
        new ExternalServiceUnavailableException(
            ExternalServiceNames.Search,
            "https://harriscounty-search.search.windows.net/ returned 503",
            new InvalidOperationException("AccountKey=abc123"),
            503),
        new ExternalServiceTimeoutException(
            ExternalServiceNames.LanguageModel,
            "deployment gpt-5-mini-prod at https://x.cognitiveservices.azure.com timed out"),
        new MalformedModelResponseException("gpt-5-mini-prod returned {\"broken\":"),
        new InvalidOperationException(
            "DefaultEndpointsProtocol=https;AccountName=harriscounty-ai-storage;AccountKey=abc123"),
        new IOException("blob sv=2025-11-05&sig=deadbeef could not be read"),
    ];
}
