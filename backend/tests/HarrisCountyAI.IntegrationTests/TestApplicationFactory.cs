using HarrisCountyAI.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting flows into the host configuration before Program.cs runs,
        // so values read during service registration (e.g. the connection string)
        // pick up the overrides.
        builder.UseSetting("Database:ApplyMigrationsAtStartup", "false");

        if (ConnectionStringOverride is not null)
        {
            builder.UseSetting("ConnectionStrings:Database", ConnectionStringOverride);
        }

        foreach (var (key, value) in BlobStorageOverrides)
        {
            builder.UseSetting($"BlobStorage:{key}", value);
        }

        // Document persistence is registered via its own extension; the
        // composition-root wiring mirrors this call.
        builder.ConfigureServices(services => services.AddDocumentPersistence());
    }
}
