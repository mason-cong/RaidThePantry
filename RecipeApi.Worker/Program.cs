using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeApi.Application.Interfaces;
using RecipeApi.Application.Services;
using RecipeApi.Infrastructure.Persistence;
using RecipeApi.Infrastructure.Repositories;
using RecipeApi.Infrastructure.Scraping;
using RecipeApi.Worker;

// Args are parsed by hand rather than handed to the host builder: its
// command-line configuration provider rejects bare verbs like "scrape".
//
// ContentRootPath is pinned to the binary's directory. The default is the
// *current* directory, so appsettings.json goes missing the moment this is run
// from anywhere but the output folder — including `dotnet run --project` and
// any scheduler, which is exactly how this is meant to be invoked.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddDbContext<RecipeDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.Configure<ScrapingOptions>(
    builder.Configuration.GetSection(ScrapingOptions.SectionName));

builder.Services.AddHttpClient<PageFetcher>((sp, client) =>
    {
        var scraping = sp.GetRequiredService<IOptions<ScrapingOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(scraping.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(scraping.UserAgent);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddHttpClient<RobotsTxtChecker>((sp, client) =>
{
    var scraping = sp.GetRequiredService<IOptions<ScrapingOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(scraping.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(scraping.UserAgent);
});

builder.Services.AddScoped<IIngredientNormalizer, IngredientNormalizer>();
builder.Services.AddScoped<IRecipeRepository, RecipeRepository>();
builder.Services.AddScoped<ScrapeJob>();
builder.Services.AddScoped<PromoteJob>();

using var host = builder.Build();

// Ctrl+C stops after the page in flight rather than mid-write.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\nStopping after the current page...");
    cancellation.Cancel();
};

var command = args.FirstOrDefault()?.ToLowerInvariant();
var flags = args.Skip(1).Where(a => a.StartsWith("--")).Select(a => a.ToLowerInvariant()).ToHashSet();
var operands = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();

using var scope = host.Services.CreateScope();

try
{
    switch (command)
    {
        case "scrape":
        {
            var urls = ResolveUrls(operands);
            if (urls.Count == 0)
            {
                Console.Error.WriteLine("No URLs given. Pass URLs directly or a file with one URL per line.");
                return 1;
            }

            Console.WriteLine($"Scraping {urls.Count} URL(s)...");
            var job = scope.ServiceProvider.GetRequiredService<ScrapeJob>();
            Console.WriteLine(await job.RunAsync(urls, flags.Contains("--refetch"), cancellation.Token));
            return 0;
        }

        case "promote":
        {
            var job = scope.ServiceProvider.GetRequiredService<PromoteJob>();
            var repromote = flags.Contains("--repromote");

            Console.WriteLine(repromote
                ? "Promoting unpromoted pages and re-promoting anything below the current parser version..."
                : "Promoting unpromoted pages...");

            Console.WriteLine(await job.RunAsync(repromote, cancellation.Token));
            return 0;
        }

        case "status":
        {
            var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();
            var rows = await db.ScrapedPages
                .GroupBy(p => new { p.Extraction, p.Promotion })
                .Select(g => new { g.Key.Extraction, g.Key.Promotion, Count = g.Count() })
                .ToListAsync(cancellation.Token);

            Console.WriteLine($"staging.ScrapedPages: {rows.Sum(r => r.Count)} row(s)");
            foreach (var row in rows.OrderBy(r => r.Extraction).ThenBy(r => r.Promotion))
                Console.WriteLine($"  {row.Extraction,-14} {row.Promotion,-12} {row.Count}");

            return 0;
        }

        default:
            Console.Error.WriteLine("""
                Usage:
                  scrape <url|file>... [--refetch]   fetch pages into staging.ScrapedPages
                  promote [--repromote]              turn staged pages into recipes
                  status                             counts by extraction/promotion state

                A file operand is read as one URL per line; blank lines and lines
                starting with # are ignored.
                """);
            return 1;
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled. Progress up to the last completed page has been saved.");
    return 130;
}
catch (Exception ex)
{
    host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Worker")
        .LogError(ex, "Command failed");
    return 1;
}

static List<string> ResolveUrls(IEnumerable<string> operands)
{
    var urls = new List<string>();

    foreach (var operand in operands)
    {
        if (File.Exists(operand))
        {
            urls.AddRange(File.ReadAllLines(operand)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#')));
        }
        else
        {
            urls.Add(operand);
        }
    }

    return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
