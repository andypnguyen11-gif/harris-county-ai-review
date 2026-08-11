using HarrisCountyAI.Application.Search.Indexing;
using HarrisCountyAI.Infrastructure.Azure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.Search;

public class SearchServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSearchIndexing(configuration);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Search:Endpoint"] = "https://unit-test.search.windows.net",
        ["Search:ApiKey"] = "unit-test-key",
        ["Search:IndexName"] = "unit-test-index",
    };

    [Fact]
    public void AddSearchIndexing_Registers_AzureDocumentIndexService()
    {
        using var provider = BuildProvider(ValidSettings());

        var service = provider.GetRequiredService<IDocumentIndexService>();

        Assert.IsType<AzureDocumentIndexService>(service);
    }

    [Fact]
    public void AddSearchIndexing_Binds_Options_From_Configuration_Section()
    {
        using var provider = BuildProvider(ValidSettings());

        var options = provider.GetRequiredService<IOptions<SearchOptions>>().Value;

        Assert.Equal("https://unit-test.search.windows.net", options.Endpoint);
        Assert.Equal("unit-test-key", options.ApiKey);
        Assert.Equal("unit-test-index", options.IndexName);
    }

    [Fact]
    public void AddSearchIndexing_Defaults_The_Index_Name()
    {
        var settings = ValidSettings();
        settings.Remove("Search:IndexName");
        using var provider = BuildProvider(settings);

        var options = provider.GetRequiredService<IOptions<SearchOptions>>().Value;

        Assert.Equal("harris-county-chunks", options.IndexName);
    }

    [Fact]
    public void AddSearchIndexing_Rejects_A_Missing_Endpoint()
    {
        var settings = ValidSettings();
        settings["Search:Endpoint"] = "";
        using var provider = BuildProvider(settings);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SearchOptions>>().Value);

        Assert.Contains("Endpoint", exception.Message);
    }

    [Fact]
    public void AddSearchIndexing_Rejects_A_Relative_Endpoint()
    {
        var settings = ValidSettings();
        settings["Search:Endpoint"] = "not-a-uri";
        using var provider = BuildProvider(settings);

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SearchOptions>>().Value);
    }

    [Fact]
    public void AddSearchIndexing_Rejects_A_Missing_ApiKey()
    {
        var settings = ValidSettings();
        settings["Search:ApiKey"] = " ";
        using var provider = BuildProvider(settings);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SearchOptions>>().Value);

        Assert.Contains("ApiKey", exception.Message);
    }

    [Fact]
    public void AddSearchIndexing_Rejects_An_Empty_IndexName()
    {
        var settings = ValidSettings();
        settings["Search:IndexName"] = "";
        using var provider = BuildProvider(settings);

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SearchOptions>>().Value);
    }
}
