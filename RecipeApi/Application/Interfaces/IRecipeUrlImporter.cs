using RecipeApi.Application.Dtos;

namespace RecipeApi.Application.Interfaces;

public enum ImportFailure
{
    None,
    InvalidUrl,
    BlockedHost,
    FetchFailed,
    NoRecipeFound
}

public record ImportedRecipeResult(
    bool Success,
    CreateRecipeRequest? Recipe,
    ImportFailure Failure,
    string? ErrorMessage)
{
    public static ImportedRecipeResult Ok(CreateRecipeRequest recipe) =>
        new(true, recipe, ImportFailure.None, null);

    public static ImportedRecipeResult Fail(ImportFailure failure, string message) =>
        new(false, null, failure, message);
}

public interface IRecipeUrlImporter
{
    /// <summary>
    /// Fetches a page and maps whatever recipe it can find. Does not persist:
    /// the API path saves immediately while the Worker stages first, and the
    /// fetch-and-parse half is all they share.
    /// </summary>
    Task<ImportedRecipeResult> ImportFromUrlAsync(string url, CancellationToken ct);
}
