using HarrisCountyAI.Infrastructure.Azure.LanguageModels;

namespace HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

public class EmbeddingOptionsValidatorTests
{
    private readonly EmbeddingOptionsValidator _validator = new();

    private static EmbeddingOptions ValidOptions() => new()
    {
        Endpoint = "https://unit-test.openai.azure.com/",
        ApiKey = "unit-test-key",
        Deployment = "text-embedding-3-small",
        MaxBatchSize = 16,
        TimeoutSeconds = 30,
    };

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var result = _validator.Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Defaults_UseBatchSizeSixteenAndThirtySecondTimeout()
    {
        var options = new EmbeddingOptions();

        Assert.Equal(16, options.MaxBatchSize);
        Assert.Equal(30, options.TimeoutSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingEndpoint_Fails(string endpoint)
    {
        var options = ValidOptions();
        options.Endpoint = endpoint;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Endpoint", result.FailureMessage);
    }

    [Fact]
    public void Validate_RelativeEndpoint_Fails()
    {
        var options = ValidOptions();
        options.Endpoint = "not-a-valid-uri";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("absolute URI", result.FailureMessage);
    }

    [Fact]
    public void Validate_MissingApiKey_Fails()
    {
        var options = ValidOptions();
        options.ApiKey = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("ApiKey", result.FailureMessage);
    }

    [Fact]
    public void Validate_MissingDeployment_Fails()
    {
        var options = ValidOptions();
        options.Deployment = " ";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Deployment", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveMaxBatchSize_Fails(int maxBatchSize)
    {
        var options = ValidOptions();
        options.MaxBatchSize = maxBatchSize;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("MaxBatchSize", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_NonPositiveTimeout_Fails(int timeoutSeconds)
    {
        var options = ValidOptions();
        options.TimeoutSeconds = timeoutSeconds;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("TimeoutSeconds", result.FailureMessage);
    }

    [Fact]
    public void Validate_MultipleProblems_ReportsAllFailures()
    {
        var options = new EmbeddingOptions { MaxBatchSize = 0 };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Endpoint", result.FailureMessage);
        Assert.Contains("ApiKey", result.FailureMessage);
        Assert.Contains("Deployment", result.FailureMessage);
        Assert.Contains("MaxBatchSize", result.FailureMessage);
    }
}
