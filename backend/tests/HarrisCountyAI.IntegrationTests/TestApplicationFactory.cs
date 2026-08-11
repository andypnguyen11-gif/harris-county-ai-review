using HarrisCountyAI.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HarrisCountyAI.IntegrationTests;

/// <summary>
/// WebApplicationFactory that skips startup database migrations and optionally
/// points the application at a test-specific database and blob storage setup.
/// </summary>
public class TestApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Connection string the application should use instead of the configured one.
    /// Set before the first client is created.
    /// </summary>
    public string? ConnectionStringOverride { get; set; }

    /// <summary>
    /// Blob storage settings the application should use instead of the
    /// configured ones (e.g. run-specific container names or a smaller
    /// maximum file size). Keys are relative to the BlobStorage section.
    /// </summary>
    public Dictionary<string, string> BlobStorageOverrides { get; } = [];

    /// <summary>
    /// Additional host configuration values, e.g. test-specific blob container
    /// names. Populate before the first client is created.
    /// </summary>
    public Dictionary<string, string?> SettingOverrides { get; } = [];

    /// <summary>
    /// Extra service registrations applied after the application's own,
    /// e.g. registrations not yet wired into the production composition root.
    /// Set before the first client is created.
    /// </summary>
    public Action<IServiceCollection>? TestServices { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting flows into the host configuration before Program.cs runs,
        // so values read during service registration (e.g. the connection string)
        // pick up the overrides.
        builder.UseSetting("Database:ApplyMigrationsAtStartup", "false");

        // Document Intelligence options are validated at startup; tests never
        // call the real service, so any well-formed values satisfy validation.
        builder.UseSetting("DocumentIntelligence:Endpoint", "https://document-intelligence.test.invalid");
        builder.UseSetting("DocumentIntelligence:ApiKey", "integration-test-key");

        // Language model options are validated at startup; tests never call the
        // real service, so any well-formed values satisfy validation.
        builder.UseSetting("LanguageModel:Endpoint", "https://language-model.test.invalid");
        builder.UseSetting("LanguageModel:ApiKey", "integration-test-key");
        builder.UseSetting("LanguageModel:Deployment", "integration-test-deployment");

        // Search options are validated at startup; tests never call the real
        // service, so any well-formed values satisfy validation.
        builder.UseSetting("Search:Endpoint", "https://search.test.invalid");
        builder.UseSetting("Search:ApiKey", "integration-test-key");

        // Embedding options are validated when the service is first resolved;
        // tests never call the real service, so any well-formed values
        // satisfy validation.
        builder.UseSetting("Embeddings:Endpoint", "https://embeddings.test.invalid");
        builder.UseSetting("Embeddings:ApiKey", "integration-test-key");
        builder.UseSetting("Embeddings:Deployment", "integration-test-deployment");

        if (ConnectionStringOverride is not null)
        {
            builder.UseSetting("ConnectionStrings:Database", ConnectionStringOverride);
        }

        foreach (var (key, value) in BlobStorageOverrides)
        {
            builder.UseSetting($"BlobStorage:{key}", value);
        }

        foreach (var (key, value) in SettingOverrides)
        {
            builder.UseSetting(key, value);
        }

        // Document and validation report persistence are registered via their
        // own extensions; the composition-root wiring mirrors these calls.
        builder.ConfigureServices(services =>
        {
            services.AddDocumentPersistence();
            services.AddValidationReports();
        });

        if (TestServices is not null)
        {
            builder.ConfigureTestServices(TestServices);
        }
    }
}
