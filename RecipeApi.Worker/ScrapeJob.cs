using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeApi.Domain.Staging;
using RecipeApi.Infrastructure.Persistence;
using RecipeApi.Infrastructure.Scraping;

namespace RecipeApi.Worker;

public record ScrapeSummary(int Fetched, int Extracted, int NoRecipe, int Failed, int Skipped, int BlockedByRobots, int Invalid)
{
    public override string ToString() =>
        $"fetched {Fetched} (recipe found {Extracted}, none {NoRecipe}, failed {Failed}), " +
        $"skipped {Skipped} already staged, {BlockedByRobots} blocked by robots.txt, {Invalid} invalid";
}

/// <summary>
/// Stage one: fetch pages into staging.ScrapedPages. Never touches Recipes.
///
/// The split exists because fetching is the expensive, rate-limited half and
/// transforming is the cheap half that will keep changing. Keeping the raw
/// recipe JSON here means a normalizer improvement replays as a local query
/// instead of a three-hour re-crawl.
/// </summary>
public class ScrapeJob(
    RecipeDbContext db,
    PageFetcher fetcher,
    RobotsTxtChecker robots,
    IOptions<ScrapingOptions> options,
    ILogger<ScrapeJob> logger)
{
    private readonly ScrapingOptions _options = options.Value;
    private readonly JsonLdRecipeExtractor _extractor = new();

    public async Task<ScrapeSummary> RunAsync(IReadOnlyList<string> urls, bool refetch, CancellationToken ct)
    {
        int fetched = 0, extracted = 0, noRecipe = 0, failed = 0, skipped = 0, blocked = 0, invalid = 0;
        var first = true;

        foreach (var rawUrl in urls)
        {
            ct.ThrowIfCancellationRequested();

            if (!FetchableUrl.TryCreate(rawUrl, out var uri, out var urlError))
            {
                logger.LogWarning("Skipping {Url}: {Error}", rawUrl, urlError);
                invalid++;
                continue;
            }

            var url = uri!.ToString();

            var existing = await db.ScrapedPages.FirstOrDefaultAsync(p => p.Url == url, ct);
            if (existing is not null && !refetch)
            {
                skipped++;
                continue;
            }

            var rules = await robots.GetRulesAsync(uri, ct);
            if (!rules.Allows(uri.PathAndQuery))
            {
                logger.LogInformation("robots.txt disallows {Url}", url);
                blocked++;
                continue;
            }

            // A site's own Crawl-delay wins whenever it asks for more than our
            // baseline. Applied before each request except the first.
            if (!first)
            {
                var delay = rules.CrawlDelay is TimeSpan crawlDelay
                    ? TimeSpan.FromSeconds(Math.Max(crawlDelay.TotalSeconds, _options.PolitenessDelaySeconds))
                    : TimeSpan.FromSeconds(_options.PolitenessDelaySeconds);

                await Task.Delay(delay, ct);
            }
            first = false;

            var page = existing ?? new ScrapedPage { Url = url };
            page.FetchedAt = DateTimeOffset.UtcNow;
            page.ExtractedJsonLd = null;
            page.RawHtml = null;
            page.ErrorMessage = null;

            try
            {
                var result = await fetcher.FetchAsync(uri, ct);
                page.HttpStatus = result.StatusCode;

                if (!result.Success)
                {
                    page.Extraction = ExtractionStatus.FetchFailed;
                    page.ErrorMessage = Truncate(result.Error, 2000);
                    failed++;
                }
                else if (_extractor.ExtractDetailed(result.Html!) is { } extraction)
                {
                    // Store the recipe node only: a few KB, and everything the
                    // promote step needs.
                    page.Extraction = ExtractionStatus.Extracted;
                    page.ExtractedJsonLd = extraction.RawJson;
                    extracted++;
                }
                else
                {
                    // Keep the whole page precisely here — the pages where
                    // extraction failed are the corpus HtmlFallbackParser gets
                    // written against.
                    page.Extraction = ExtractionStatus.NoRecipeFound;
                    page.RawHtml = result.Html;
                    noRecipe++;
                }

                fetched++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // One bad page must not end the batch.
                logger.LogWarning(ex, "Fetch threw for {Url}", url);
                page.Extraction = ExtractionStatus.FetchFailed;
                page.ErrorMessage = Truncate(ex.Message, 2000);
                failed++;
            }

            // Promotion state is deliberately reset on a refetch: new content
            // deserves a fresh transform.
            page.Promotion = PromotionStatus.NotPromoted;
            page.ParserVersion = 0;

            if (existing is null)
                db.ScrapedPages.Add(page);

            await db.SaveChangesAsync(ct);
        }

        return new ScrapeSummary(fetched, extracted, noRecipe, failed, skipped, blocked, invalid);
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
