using HarrisCountyAI.Application.Documents;
using HarrisCountyAI.Infrastructure.Azure.BlobStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HarrisCountyAI.UnitTests.BlobStorage;

public class BlobStorageServiceExtensionsTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddBlobStorage(configuration);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
        ["BlobStorage:CaseDocumentsContainerName"] = "case-documents",
        ["BlobStorage:KnowledgeBaseContainerName"] = "knowledge-base",
        ["BlobStorage:MaxFileSizeBytes"] = "1048576",
    };

    [Fact]
    public void AddBlobStorage_Registers_AzureBlobDocumentStorageService()
    {
        using var provider = BuildProvider(ValidSettings());

        var service = provider.GetRequiredService<IDocumentStorageService>();

        Assert.IsType<AzureBlobDocumentStorageService>(service);
    }

    [Fact]
    public void AddBlobStorage_Binds_Options_From_Configuration_Section()
    {
        using var provider = BuildProvider(ValidSettings());

        var options = provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value;

        Assert.Equal("UseDevelopmentStorage=true", options.ConnectionString);
        Assert.Equal("case-documents", options.CaseDocumentsContainerName);
        Assert.Equal("knowledge-base", options.KnowledgeBaseContainerName);
        Assert.Equal(1_048_576, options.MaxFileSizeBytes);
    }

    [Fact]
    public void AddBlobStorage_Uses_Defaults_For_Optional_Settings()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
        });

        var options = provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value;

        Assert.Equal("case-documents", options.CaseDocumentsContainerName);
        Assert.Equal("knowledge-base", options.KnowledgeBaseContainerName);
        Assert.Equal(52_428_800, options.MaxFileSizeBytes);
    }

    [Fact]
    public void AddBlobStorage_Configures_Validator_With_MaxFileSize_From_Configuration()
    {
        using var provider = BuildProvider(ValidSettings());

        var validator = provider.GetRequiredService<DocumentFileValidator>();

        Assert.Equal(1_048_576, validator.MaxFileSizeBytes);
    }

    [Fact]
    public void AddBlobStorage_Rejects_Missing_ConnectionString()
    {
        var settings = ValidSettings();
        settings["BlobStorage:ConnectionString"] = "";
        using var provider = BuildProvider(settings);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value);

        Assert.Contains("ConnectionString", exception.Message);
    }

    [Fact]
    public void AddBlobStorage_Rejects_NonPositive_MaxFileSize()
    {
        var settings = ValidSettings();
        settings["BlobStorage:MaxFileSizeBytes"] = "0";
        using var provider = BuildProvider(settings);

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value);
    }

    [Fact]
    public void AddBlobStorage_Rejects_Empty_Container_Names()
    {
        var settings = ValidSettings();
        settings["BlobStorage:CaseDocumentsContainerName"] = " ";
        using var provider = BuildProvider(settings);

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value);
    }
}
