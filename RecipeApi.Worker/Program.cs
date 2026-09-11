using System.Net;
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
        ScrapingHttpDefaults.Apply(client, sp.GetRequiredService<IOptions<ScrapingOptions>>().Value))
    // Was a bare HttpClientHandler, which meant the bulk crawl — the thing that
    // fetches the most URLs by far — had none of the SSRF protection the API's
    // single-import endpoint got. Both now build the handler the same way.
    .ConfigurePrimaryHttpMessageHandler(sp =>
        ScrapingHttpDefaults.Handler(sp.GetRequiredService<IOptions<ScrapingOptions>>().Value));

builder.Services.AddHttpClient<RobotsTxtChecker>((sp, client) =>
        ScrapingHttpDefaults.Apply(client, sp.GetRequiredService<IOptions<ScrapingOptions>>().Value))
    // robots.txt is fetched from a host the crawl list chose, so it needs the
    // same guard. Redirects stay automatic here — RFC 9309 expects them to be
    // followed — which is safe because the guard runs per connection.
    .ConfigurePrimaryHttpMessageHandler(sp =>
        ScrapingHttpDefaults.Handler(sp.GetRequiredService<IOptions<ScrapingOptions>>().Value, followRedirects: true));

// longTimeout because sitemaps are megabytes, not pages.
builder.Services.AddHttpClient<SitemapDiscovery>((sp, client) =>
        ScrapingHttpDefaults.Apply(
            client, sp.GetRequiredService<IOptions<ScrapingOptions>>().Value, longTimeout: true))
    .ConfigurePrimaryHttpMessageHandler(sp =>
        ScrapingHttpDefaults.Handler(sp.GetRequiredService<IOptions<ScrapingOptions>>().Value, followRedirects: true));

builder.Services.AddScoped<IIngredientNormalizer, IngredientNormalizer>();
builder.Services.AddScoped<IRecipeRepository, RecipeRepository>();
builder.Services.AddScoped<DiscoverJob>();
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

// Flags that take a value, in either "--limit 100" or "--limit=100" form.
// Without this list the bare-flag reading treats "100" as an operand, and
// `discover <site> --limit 100` quietly tries to discover two sites.
var valueFlags = new HashSet<string> { "--limit", "--match", "--out" };

var flags = new HashSet<string>();
var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var operands = new List<string>();

var rest = args.Skip(1).ToList();
for (var i = 0; i < rest.Count; i++)
{
    var arg = rest[i];

    if (!arg.StartsWith("--"))
    {
        operands.Add(arg);
        continue;
    }

    var equals = arg.IndexOf('=');
    if (equals > 0)
    {
        var key = arg[..equals].ToLowerInvariant();
        flags.Add(key);
        values[key] = arg[(equals + 1)..];
        continue;
    }

    var name = arg.ToLowerInvariant();
    flags.Add(name);

    if (valueFlags.Contains(name) && i + 1 < rest.Count && !rest[i + 1].StartsWith("--"))
        values[name] = rest[++i];
}

using var scope = host.Services.CreateScope();

try
{
    switch (command)
    {
        case "discover":
        {
            if (operands.Count == 0 ||
                !Uri.TryCreate(operands[0], UriKind.Absolute, out var site) ||
                (site.Scheme != Uri.UriSchemeHttp && site.Scheme != Uri.UriSchemeHttps))
            {
                Console.Error.WriteLine("Pass a site, e.g. discover https://www.example.com --match /recipe/");
                return 1;
            }

            // Capped by default. Recipe sitemaps run to tens of thousands of
            // URLs, so an uncapped run hands `scrape` days of requests against
            // somebody else's origin — "--limit 0" has to be typed on purpose.
            var limit = values.TryGetValue("--limit", out var rawLimit) && int.TryParse(rawLimit, out var parsed)
                ? parsed
                : 100;

            var match = values.GetValueOrDefault("--match");
            var output = values.GetValueOrDefault("--out");

            Console.WriteLine($"Reading sitemaps for {site.Host}" +
                              (match is null ? "" : $", matching \"{match}\"") +
                              (limit > 0 ? $", up to {limit} url(s)..." : ", with no cap..."));

            var job = scope.ServiceProvider.GetRequiredService<DiscoverJob>();
            var result = await job.RunAsync(site, new DiscoveryRequest(limit, match), output, cancellation.Token);

            Console.WriteLine(result);

            // Without --out the list goes to stdout, so it can be piped or eyeballed.
            if (output is null)
                foreach (var url in result.Urls)
                    Console.WriteLine(url);

            return 0;
        }

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
                  discover <site> [--match S] [--limit N] [--out F]
                                                     read the site's sitemaps for pages to fetch
                  scrape <url|file>... [--refetch]   fetch pages into staging.ScrapedPages
                  promote [--repromote]              turn staged pages into recipes
                  status                             counts by extraction/promotion state

                A file operand is read as one URL per line; blank lines and lines
                starting with # are ignored.

                discover writes nothing to the database. It reads robots.txt for the
                site's declared sitemaps, walks them, keeps URLs containing --match
                (e.g. "/recipe/") that robots.txt permits, and stops at --limit,
                which defaults to 100. "--limit 0" lifts the cap; recipe sitemaps
                hold tens of thousands of URLs, so that is a deliberate choice
                rather than a default.

                  discover https://www.example.com --match /recipe/ --out crawl/urls.txt
                  scrape crawl/urls.txt
                  promote
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
