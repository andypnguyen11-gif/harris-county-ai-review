using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HarrisCountyAI.IntegrationTests;

/// <summary>
/// WebApplicationFactory that skips startup database migrations and optionally
/// points the application at a test-specific database.
/// </summary>
public class TestApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Connection string the application should use instead of the configured one.
    /// Set before the first client is created.
    /// </summary>
    public string? ConnectionStringOverride { get; set; }

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
    }
}
