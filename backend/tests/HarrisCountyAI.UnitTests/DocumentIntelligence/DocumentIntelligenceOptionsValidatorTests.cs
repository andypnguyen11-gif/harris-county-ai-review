using HarrisCountyAI.Infrastructure.Azure.DocumentIntelligence;

namespace HarrisCountyAI.UnitTests.DocumentIntelligence;

public class DocumentIntelligenceOptionsValidatorTests
{
    private readonly DocumentIntelligenceOptionsValidator _validator = new();

    private static DocumentIntelligenceOptions ValidOptions() => new()
    {
        Endpoint = "https://example.cognitiveservices.azure.com/",
        ApiKey = "test-key",
        ModelId = "prebuilt-layout",
        TimeoutSeconds = 120,
    };

    [Fact]
    public void Valid_Options_Pass()
    {
        var result = _validator.Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Defaults_Provide_Model_And_Timeout()
    {
        var options = new DocumentIntelligenceOptions();

        Assert.Equal("prebuilt-layout", options.ModelId);
        Assert.Equal(120, options.TimeoutSeconds);
    }

    [Fact]
    public void Missing_Endpoint_Fails()
    {
        var options = ValidOptions();
        options.Endpoint = "";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Endpoint"));
    }

    [Fact]
    public void Relative_Endpoint_Fails()
    {
        var options = ValidOptions();
        options.Endpoint = "not-a-url";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Endpoint"));
    }

    [Fact]
    public void Missing_ApiKey_Fails()
    {
        var options = ValidOptions();
        options.ApiKey = "  ";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("ApiKey"));
    }

    [Fact]
    public void Missing_ModelId_Fails()
    {
        var options = ValidOptions();
        options.ModelId = "";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("ModelId"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_Positive_Timeout_Fails(int timeoutSeconds)
    {
        var options = ValidOptions();
        options.TimeoutSeconds = timeoutSeconds;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("TimeoutSeconds"));
    }

    [Fact]
    public void Reports_All_Failures_At_Once()
    {
        var result = _validator.Validate(null, new DocumentIntelligenceOptions { ModelId = "", TimeoutSeconds = 0 });

        Assert.True(result.Failed);
        Assert.Equal(4, result.Failures!.Count());
    }
}
