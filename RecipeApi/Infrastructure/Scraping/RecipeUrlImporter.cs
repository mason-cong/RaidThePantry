using RecipeApi.Application.Interfaces;

namespace RecipeApi.Infrastructure.Scraping;

public class RecipeUrlImporter(
    PageFetcher fetcher,
    ILogger<RecipeUrlImporter> logger) : IRecipeUrlImporter
{
    private readonly JsonLdRecipeExtractor _jsonLd = new();
    private readonly HtmlFallbackParser _fallback = new();

    public async Task<ImportedRecipeResult> ImportFromUrlAsync(string url, CancellationToken ct)
    {
        if (!FetchableUrl.TryCreate(url, out var uri, out var urlError))
            return ImportedRecipeResult.Fail(ImportFailure.InvalidUrl, urlError!);

        PageFetchResult fetched;
        try
        {
            fetched = await fetcher.FetchAsync(uri!, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ImportedRecipeResult.Fail(ImportFailure.FetchFailed, "The site took too long to respond.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogInformation(ex, "Fetch failed for {Url}", uri);
            return ImportedRecipeResult.Fail(ImportFailure.FetchFailed, "Could not reach that URL.");
        }

        if (!fetched.Success)
            return ImportedRecipeResult.Fail(fetched.Failure, fetched.Error!);

        var extracted = _jsonLd.Extract(fetched.Html!) ?? _fallback.Extract(fetched.Html!);

        if (extracted is null || !extracted.IsUsable)
        {
            return ImportedRecipeResult.Fail(
                ImportFailure.NoRecipeFound,
                "No recipe data was found on that page.");
        }

        return ImportedRecipeResult.Ok(SchemaOrgRecipeMapper.ToCreateRequest(extracted));
    }
}
