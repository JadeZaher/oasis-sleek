using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Providers;

namespace OASIS.WebAPI.IntegrationTests.Factories;

/// <summary>
/// Test host that keeps the real PostgreSQL provider (the persistent local
/// <c>oasis</c> database from <c>ConnectionStrings:OASISDatabase</c>) so
/// <c>Database.Migrate()</c> runs for real and the schema/data persist across
/// runs. Only auth and the in-memory storage provider are swapped for tests.
/// Requires the local Postgres container to be running (tests/run-tests.ps1
/// spins it up automatically).
/// </summary>
public class OASISTestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "super-secret-test-key-that-is-long-enough!",
                ["Jwt:Issuer"] = "test",
                ["Jwt:Audience"] = "test",
                ["OASIS:DefaultProvider"] = "PostgreSQL"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

            var inMemoryProvider = services.SingleOrDefault(d => d.ImplementationType == typeof(InMemoryStorageProvider));
            if (inMemoryProvider != null) services.Remove(inMemoryProvider);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, "true");
        return client;
    }
}
