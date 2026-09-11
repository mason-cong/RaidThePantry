using Microsoft.Extensions.Logging;
using RecipeApi.Infrastructure.Scraping;

namespace RecipeApi.Worker;

/// <summary>
/// Stage zero: work out which pages are worth fetching, and write the list out.
///
/// It deliberately writes a file instead of feeding `scrape` directly. The list
/// is the reviewable artefact — you can read it, cut it down, and re-run the
/// crawl from it without discovering again — and it keeps this stage in line
/// with the other two, each of which can be run and re-run on its own.
/// </summary>
public class DiscoverJob(SitemapDiscovery discovery, ILogger<DiscoverJob> logger)
{
    public async Task<DiscoveryResult> RunAsync(
        Uri site, DiscoveryRequest request, string? outputPath, CancellationToken ct)
    {
        var result = await discovery.DiscoverAsync(site, request, ct);

        if (outputPath is not null)
        {
            // Appending rather than overwriting, so discovering several sites in
            // turn builds one crawl list. `scrape` de-duplicates anyway.
            await File.AppendAllLinesAsync(
                outputPath,
                [$"# {site.Host} — {result.Urls.Count} url(s), {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}Z", .. result.Urls, string.Empty],
                ct);

            logger.LogInformation("Wrote {Count} url(s) to {Path}", result.Urls.Count, outputPath);
        }

        return result;
    }
}
