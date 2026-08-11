using HarrisCountyAI.Application.Common.AI;
using HarrisCountyAI.Infrastructure.Azure.LanguageModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Infrastructure.Azure.LanguageModels;

public class LanguageModelServiceExtensionsTests
{
    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> CreateValidValues() => new()
    {
        ["LanguageModel:Endpoint"] = "https://unit-test.openai.azure.com/",
        ["LanguageModel:ApiKey"] = "unit-test-key",
        ["LanguageModel:Deployment"] = "gpt-unit-test",
    };

    private static ServiceProvider BuildProvider(Dictionary<string, string?> configValues)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLanguageModel(CreateConfiguration(configValues));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddLanguageModel_Registers_Azure_Implementation()
    {
        using var provider = BuildProvider(CreateValidValues());

        var service = provider.GetRequiredService<ILanguageModelService>();

        Assert.IsType<AzureLanguageModelService>(service);
    }

    [Fact]
    public void AddLanguageModel_Binds_Options_From_LanguageModel_Section()
    {
        var values = CreateValidValues();
        values["LanguageModel:TimeoutSeconds"] = "15";
        values["LanguageModel:MaxOutputTokens"] = "512";
        using var provider = BuildProvider(values);

        var options = provider.GetRequiredService<IOptions<LanguageModelOptions>>().Value;

        Assert.Equal("https://unit-test.openai.azure.com/", options.Endpoint);
        Assert.Equal("unit-test-key", options.ApiKey);
        Assert.Equal("gpt-unit-test", options.Deployment);
        Assert.Equal(15, options.TimeoutSeconds);
        Assert.Equal(512, options.MaxOutputTokens);
    }

    [Fact]
    public void AddLanguageModel_Applies_Defaults_When_Optional_Values_Are_Omitted()
    {
        using var provider = BuildProvider(CreateValidValues());

        var options = provider.GetRequiredService<IOptions<LanguageModelOptions>>().Value;

        Assert.Equal(60, options.TimeoutSeconds);
        Assert.Equal(1024, options.MaxOutputTokens);
    }

    [Fact]
    public void Resolving_Options_Without_Endpoint_Fails_Fast_With_Clear_Message()
    {
        var values = CreateValidValues();
        values.Remove("LanguageModel:Endpoint");
        using var provider = BuildProvider(values);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<LanguageModelOptions>>().Value);

        Assert.Contains("LanguageModel:Endpoint is required", exception.Message);
    }

    [Fact]
    public void Resolving_Options_Without_ApiKey_Fails_Fast_With_Clear_Message()
    {
        var values = CreateValidValues();
        values.Remove("LanguageModel:ApiKey");
        using var provider = BuildProvider(values);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<LanguageModelOptions>>().Value);

        Assert.Contains("LanguageModel:ApiKey is required", exception.Message);
    }

    [Fact]
    public void Resolving_Service_With_Invalid_Configuration_Fails_Fast()
    {
        using var provider = BuildProvider([]);

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<ILanguageModelService>());
    }

    [Fact]
    public void AddLanguageModel_Registers_Startup_Validation()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLanguageModel(CreateConfiguration([]));
        using var provider = services.BuildServiceProvider();

        var startupValidator = provider.GetService<IStartupValidator>();

        Assert.NotNull(startupValidator);
        Assert.Throws<OptionsValidationException>(startupValidator.Validate);
    }
}
