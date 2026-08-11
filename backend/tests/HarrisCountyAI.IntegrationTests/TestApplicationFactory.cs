using HarrisCountyAI.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
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

    /// <summary>
    /// The local-development settings the suite runs against — the SQL Server
    /// and Azurite endpoints from docker compose — read from the copy of
    /// appsettings.Development.json in the test output directory and replayed
    /// into the host configuration.
    /// </summary>
    /// <remarks>
    /// The application discovers its own appsettings files through a physical
    /// file provider rooted at whatever content root the host resolves, and
    /// only loads the Development file when the ambient environment says
    /// Development. Neither holds reliably under a test host: the file
    /// provider silently returns nothing when the repository lives beneath a
    /// dot-directory (a git worktree under <c>.claude/</c>, for instance), and
    /// the environment depends on the shell that launched the run. Reading the
    /// file directly and pushing its values through <c>UseSetting</c> — which
    /// does reach the host configuration before Program.cs reads it — makes
    /// the suite behave the same wherever it runs. Per-test overrides are
    /// applied afterwards and still win.
    /// </remarks>
    private static readonly IReadOnlyList<KeyValuePair<string, string>> LocalDevelopmentSettings =
        LoadLocalDevelopmentSettings();

    private static IReadOnlyList<KeyValuePair<string, string>> LoadLocalDevelopmentSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        if (!File.Exists(path))
        {
            return [];
        }

        // AddJsonStream, not AddJsonFile: the stream is opened here, so the
        // configuration provider never consults a file provider that might
        // refuse to serve the path.
        using var stream = File.OpenRead(path);
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        return
        [
            .. configuration.AsEnumerable()
                .Where(entry => entry.Value is not null)
                .Select(entry => new KeyValuePair<string, string>(entry.Key, entry.Value!)),
        ];
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Keeps app.Environment.IsDevelopment() true, matching how the suite
        // has always run, without relying on the launching shell's environment.
        builder.UseEnvironment("Development");

        foreach (var (key, value) in LocalDevelopmentSettings)
        {
            builder.UseSetting(key, value);
        }

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
