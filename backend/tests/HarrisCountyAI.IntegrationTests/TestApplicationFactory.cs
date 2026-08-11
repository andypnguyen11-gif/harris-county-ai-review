using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
        var settings = new Dictionary<string, string?>
        {
            ["Database:ApplyMigrationsAtStartup"] = "false",
        };

        if (ConnectionStringOverride is not null)
        {
            settings["ConnectionStrings:Database"] = ConnectionStringOverride;
        }

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
    }
}
