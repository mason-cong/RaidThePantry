using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;
using RecipeApi.Domain.Staging;
using RecipeApi.Infrastructure.Persistence;
using RecipeApi.Infrastructure.Scraping;

namespace RecipeApi.Worker;

public record PromoteSummary(int Created, int Updated, int Linked, int Rejected)
{
    public override string ToString() =>
        $"created {Created}, updated {Updated}, linked to existing {Linked}, rejected {Rejected}";
}

/// <summary>
/// Stage two: turn staged pages into recipes. No network calls at all, so it is
/// fast and freely re-runnable across the whole corpus — which is the point of
/// having staged the payload in the first place.
/// </summary>
public class PromoteJob(
    RecipeDbContext db,
    IRecipeRepository repository,
    IOptions<ScrapingOptions> options,
    ILogger<PromoteJob> logger)
{
    private readonly ScrapingOptions _options = options.Value;
    private readonly JsonLdRecipeExtractor _extractor = new();

    public async Task<PromoteSummary> RunAsync(bool repromote, CancellationToken ct)
    {
        var version = _options.ParserVersion;

        var query = db.ScrapedPages.Where(p => p.Extraction == ExtractionStatus.Extracted);

        query = repromote
            // Rejected rows stay rejected: they failed to map, and re-running the
            // same transform on the same bytes will fail the same way.
            ? query.Where(p => p.Promotion == PromotionStatus.NotPromoted ||
                              (p.Promotion == PromotionStatus.Promoted && p.ParserVersion < version))
            : query.Where(p => p.Promotion == PromotionStatus.NotPromoted);

        var pages = await query.OrderBy(p => p.FetchedAt).ToListAsync(ct);

        int created = 0, updated = 0, linked = 0, rejected = 0;

        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();

            var recipe = _extractor.FromRecipeJson(page.ExtractedJsonLd);
            if (recipe is null)
            {
                page.Promotion = PromotionStatus.Rejected;
                page.ErrorMessage = "Staged JSON no longer maps to a usable recipe.";
                await db.SaveChangesAsync(ct);
                rejected++;
                continue;
            }

            var request = SchemaOrgRecipeMapper.ToCreateRequest(recipe);

            try
            {
                if (page.PromotedRecipeId is Guid existingId)
                {
                    // In-place update keeps the recipe id stable, so favorites and
                    // any external links survive a re-promote.
                    var result = await repository.ReplaceScrapedAsync(existingId, request, ct);

                    switch (result)
                    {
                        case WriteResult.Success:
                            updated++;
                            break;

                        case WriteResult.NotFound:
                            // Deleted since it was promoted; fall back to creating.
                            page.PromotedRecipeId = await repository.CreateAsync(
                                request, null, RecipeSourceType.BulkScrape, page.Url, ct);
                            created++;
                            break;

                        default:
                            // Someone claimed it. Never overwrite an owned recipe.
                            page.Promotion = PromotionStatus.Rejected;
                            page.ErrorMessage = "Target recipe now has an owner; refusing to overwrite.";
                            await db.SaveChangesAsync(ct);
                            rejected++;
                            continue;
                    }
                }
                else if (await repository.FindIdBySourceUrlAsync(page.Url, ct) is Guid alreadyImported)
                {
                    // A user imported this URL through the API first. Adopt it
                    // rather than fighting the unique index over it.
                    page.PromotedRecipeId = alreadyImported;
                    linked++;
                }
                else
                {
                    // CreatedByUserId stays null: scraped recipes have no owner,
                    // which is what makes them read-only through the API.
                    page.PromotedRecipeId = await repository.CreateAsync(
                        request, null, RecipeSourceType.BulkScrape, page.Url, ct);
                    created++;
                }

                page.Promotion = PromotionStatus.Promoted;
                page.ParserVersion = version;
                page.ErrorMessage = null;
                await db.SaveChangesAsync(ct);
            }
            catch (DuplicateSourceUrlException)
            {
                page.PromotedRecipeId = await repository.FindIdBySourceUrlAsync(page.Url, ct);
                page.Promotion = PromotionStatus.Promoted;
                page.ParserVersion = version;
                await db.SaveChangesAsync(ct);
                linked++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Promote failed for {Url}", page.Url);
                page.Promotion = PromotionStatus.Rejected;
                page.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                await db.SaveChangesAsync(ct);
                rejected++;
            }
        }

        return new PromoteSummary(created, updated, linked, rejected);
    }
}
