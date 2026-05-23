using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OASIS.WebAPI.IntegrationTests.Factories;

/// <summary>
/// Test host for integration tests against a real SurrealDB container.
///
/// Design:
///   - NO EF InMemory swap. NO OASISDbContext.
///   - NO db.Database.Migrate() (that was a relational-only boot path, removed here).
///   - Authentication is replaced by the test-only TestAuthHandler so tests
///     can exercise auth-gated endpoints without real JWT tokens.
///   - The SurrealDB connection is read from the environment or defaults to
///     the local test container on port 8442 (started by start-test-container.ps1).
///   - Per-test namespace isolation is owned by IntegrationTestBase — the
///     factory itself is shared across the test class collection (IClassFixture).
///
/// Storage backend wiring:
///   When the SurrealDB adapter (Worker B: ISurrealDbRepository) exists it will
///   be registered in Program.cs via the IStorageProvider seam. Until then the
///   existing EF-backed adapters remain wired from Program.cs — that is fine
///   because the factory does NOT swap the storage, only the auth scheme.
///   The adapter swap is wave 2 (tasks 5–8 of the migration plan).
/// </summary>
public class OASISTestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Minimal JWT config so the JWT middleware initialises without
            // throwing; the TestAuthHandler takes over auth in the test pipeline.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]      = "super-secret-test-key-that-is-long-enough!",
                ["Jwt:Issuer"]   = "test",
                ["Jwt:Audience"] = "test",

                // SurrealDB connection for the test host (wave 2: adapter wiring).
                // Override with OASIS_SURREAL_TEST_URL environment variable in CI.
                ["SurrealDb:Endpoint"] = Environment.GetEnvironmentVariable("OASIS_SURREAL_TEST_URL")
                                         ?? "http://localhost:8442",
                ["SurrealDb:Username"] = Environment.GetEnvironmentVariable("OASIS_SURREAL_TEST_USER")
                                         ?? "root",
                ["SurrealDb:Password"] = Environment.GetEnvironmentVariable("OASIS_SURREAL_TEST_PASS")
                                         ?? "oasis-surreal-root",

                // Keep the OASIS provider key so Program.cs provider-selection code
                // (if any) doesn't throw on missing config.
                ["OASIS:DefaultProvider"] = "SurrealDb"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the real auth pipeline with the test-only handler.
            // All requests that include the X-Test-Auth: true header are
            // automatically authenticated as the default test avatar.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme    = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

            // Wave 2 placeholder: when Worker B's ISurrealDbRepository is
            // registered in Program.cs, add any test-overrides here.
            // For now the EF-backed adapters from Program.cs remain wired.
        });
    }

    /// Create an HTTP client pre-configured with the test-auth header.
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, "true");
        return client;
    }
}
