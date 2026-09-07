using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace RecipeApi.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` at design time. Without it the tools have to boot the
/// full web host to resolve a DbContext, which breaks as soon as Program.cs starts
/// requiring services that aren't available offline.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RecipeDbContext>
{
    public RecipeDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5433;Database=recipefinder;Username=recipeapi;Password=localdev";

        var options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new RecipeDbContext(options);
    }
}
