using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RecipeApi.Infrastructure.Persistence;

namespace RecipeApi.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real application in-process against a real PostgreSQL database.
///
/// Nothing is substituted for a fake: the point of these tests is that the query
/// translation, the trigram indexes, the unique constraints and the cascades all
/// behave, and none of that exists in an in-memory provider. The four search
/// bugs this suite guards against would every one of them pass against a fake.
/// </summary>
public class RecipeApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// A database of its own, so a test run never disturbs the development seed.
    /// Created on first use and re-seeded between test classes.
    /// </summary>
    private const string TestDatabase = "recipefinder_test";

    // Fixed so tokens stay valid for the life of the run. Test-only value; the
    // real key comes from user-secrets and is never checked in.
    private const string TestSigningKey = "dGVzdC1vbmx5LXNpZ25pbmcta2V5LTMyLWJ5dGVzKys=";

    private readonly string _adminConnectionString;

    public string ConnectionString { get; }

    public RecipeApiFactory()
    {
        var baseConnectionString =
            Environment.GetEnvironmentVariable("RECIPEFINDER_TEST_POSTGRES")
            ?? "Host=localhost;Port=5433;Username=recipeapi;Password=localdev";

        // Port 5433 by default, matching docker-compose.yml — a local PostgreSQL
        // service on 5432 would otherwise answer instead.
        _adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;

        ConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = TestDatabase
        }.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                ["Jwt:SigningKey"] = TestSigningKey,
                // The import tests need to reach a fixture site on loopback, which
                // the SSRF guard blocks by default. Enabled only here.
                ["Scraping:AllowLoopbackHosts"] = "true",
                ["Scraping:PolitenessDelaySeconds"] = "0"
            }));
    }

    /// <summary>Creates the database if absent, then applies migrations.</summary>
    public async Task InitializeAsync()
    {
        await EnsureDatabaseExistsAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();

        // Runs the same migration the application uses, including the raw SQL for
        // pg_trgm and the GIN indexes.
        await db.Database.MigrateAsync();

        await ResetAsync();
    }

    /// <summary>
    /// Returns the database to the known 18-recipe seed. Called before each test
    /// class so classes cannot leak state into one another.
    /// </summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();

        var stdout = Console.Out;
        Console.SetOut(TextWriter.Null);   // the seeder narrates; tests do not need it
        try
        {
            await DbSeeder.SeedAsync(db, reset: true);
        }
        finally
        {
            Console.SetOut(stdout);
        }

        // Accounts are not the seeder's business — it must never delete users in a
        // real database. Here they are throwaway, and every test registers new
        // ones, so without this the table grows by a few hundred rows per run.
        await db.Users.ExecuteDeleteAsync();
    }

    private async Task EnsureDatabaseExistsAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);

        try
        {
            await connection.OpenAsync();
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                $"Could not reach PostgreSQL at {new NpgsqlConnectionStringBuilder(_adminConnectionString).Host}:" +
                $"{new NpgsqlConnectionStringBuilder(_adminConnectionString).Port}. " +
                "These are integration tests against a live database — start it with `docker compose up -d`, " +
                "or point RECIPEFINDER_TEST_POSTGRES at another server.", ex);
        }

        await using var exists = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", TestDatabase);

        if (await exists.ExecuteScalarAsync() is not null)
            return;

        // Identifier is a compile-time constant, so interpolation is safe here;
        // CREATE DATABASE cannot take a parameter.
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{TestDatabase}\"", connection);
        await create.ExecuteNonQueryAsync();
    }
}
