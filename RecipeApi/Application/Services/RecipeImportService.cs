using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;

namespace RecipeApi.Application.Services;

public enum ImportOutcome
{
    Created,
    Duplicate,
    InvalidUrl,
    BlockedHost,
    FetchFailed,
    NoRecipeFound
}

public record ImportResult(ImportOutcome Outcome, Guid? RecipeId, string? Error);

public class RecipeImportService(IRecipeUrlImporter importer, IRecipeRepository repository)
{
    /// <summary>
    /// Synchronous by design. A caller is waiting on this response, so it fetches,
    /// parses and saves in one go and hands back the recipe. The Worker's bulk
    /// path stages the raw payload instead and promotes later — same extraction,
    /// different persistence, because the constraints are different.
    /// </summary>
    public async Task<ImportResult> ImportAsync(string url, Guid userId, CancellationToken ct)
    {
        var extracted = await importer.ImportFromUrlAsync(url, ct);

        if (!extracted.Success)
        {
            var outcome = extracted.Failure switch
            {
                ImportFailure.InvalidUrl => ImportOutcome.InvalidUrl,
                ImportFailure.BlockedHost => ImportOutcome.BlockedHost,
                ImportFailure.NoRecipeFound => ImportOutcome.NoRecipeFound,
                _ => ImportOutcome.FetchFailed
            };

            return new ImportResult(outcome, null, extracted.ErrorMessage);
        }

        var sourceUrl = url.Trim();

        // Cheap path: already imported, so hand back what exists rather than
        // letting the unique index turn a repeat click into a 500.
        if (await repository.FindIdBySourceUrlAsync(sourceUrl, ct) is Guid existing)
            return new ImportResult(ImportOutcome.Duplicate, existing, null);

        try
        {
            var id = await repository.CreateAsync(
                extracted.Recipe!, userId, RecipeSourceType.UrlImport, sourceUrl, ct);

            return new ImportResult(ImportOutcome.Created, id, null);
        }
        catch (DuplicateSourceUrlException)
        {
            // Lost the race between the check above and the insert.
            var winner = await repository.FindIdBySourceUrlAsync(sourceUrl, ct);
            return new ImportResult(ImportOutcome.Duplicate, winner, null);
        }
    }
}
