using HarrisCountyAI.Infrastructure.Azure.LanguageModels;

namespace HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

public class LanguageModelOptionsValidatorTests
{
    private readonly LanguageModelOptionsValidator _validator = new();

    private static LanguageModelOptions CreateValidOptions() => new()
    {
        Endpoint = "https://unit-test.openai.azure.com/",
        ApiKey = "unit-test-key",
        Deployment = "gpt-unit-test",
    };

    [Fact]
    public void Valid_Options_Pass()
    {
        var result = _validator.Validate(null, CreateValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Defaults_Are_Sixty_Second_Timeout_And_1024_Max_Output_Tokens()
    {
        var options = new LanguageModelOptions();

        Assert.Equal(60, options.TimeoutSeconds);
        Assert.Equal(1024, options.MaxOutputTokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_Endpoint_Fails_With_Clear_Message(string? endpoint)
    {
        var options = CreateValidOptions();
        options.Endpoint = endpoint!;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("LanguageModel:Endpoint is required", result.FailureMessage);
    }

    [Fact]
    public void Malformed_Endpoint_Fails_With_Clear_Message()
    {
        var options = CreateValidOptions();
        options.Endpoint = "not-a-url";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("not a valid absolute http(s) URL", result.FailureMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_ApiKey_Fails_With_Clear_Message(string? apiKey)
    {
        var options = CreateValidOptions();
        options.ApiKey = apiKey!;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("LanguageModel:ApiKey is required", result.FailureMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_Deployment_Fails_With_Clear_Message(string? deployment)
    {
        var options = CreateValidOptions();
        options.Deployment = deployment!;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("LanguageModel:Deployment is required", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositive_Timeout_Fails(int timeoutSeconds)
    {
        var options = CreateValidOptions();
        options.TimeoutSeconds = timeoutSeconds;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("LanguageModel:TimeoutSeconds must be greater than zero", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositive_MaxOutputTokens_Fails(int maxOutputTokens)
    {
        var options = CreateValidOptions();
        options.MaxOutputTokens = maxOutputTokens;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("LanguageModel:MaxOutputTokens must be greater than zero", result.FailureMessage);
    }

    [Fact]
    public void Empty_Options_Report_All_Missing_Values_At_Once()
    {
        var result = _validator.Validate(null, new LanguageModelOptions());

        Assert.True(result.Failed);
        Assert.Contains("LanguageModel:Endpoint", result.FailureMessage);
        Assert.Contains("LanguageModel:ApiKey", result.FailureMessage);
        Assert.Contains("LanguageModel:Deployment", result.FailureMessage);
    }
}
