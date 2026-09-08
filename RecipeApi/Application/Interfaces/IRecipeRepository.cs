using RecipeApi.Application.Dtos;
using RecipeApi.Domain;

namespace RecipeApi.Application.Interfaces;

/// <summary>
/// Distinguishes "no such recipe" from "not yours", which a bool return cannot.
/// The ownership rule needs both: 404 and 403 are different answers.
/// </summary>
public enum WriteResult
{
    Success,
    NotFound,
    Forbidden
}

public interface IRecipeRepository
{
    Task<PagedResult<RecipeSummaryDto>> SearchAsync(RecipeSearchQuery query, CancellationToken ct);

    /// <param name="currentUserId">Null for guests; drives RecipeDetailDto.IsEditable.</param>
    Task<RecipeDetailDto?> GetByIdAsync(Guid id, Guid? currentUserId, CancellationToken ct);

    /// <summary>
    /// Resolves ingredient/cuisine/tag names to existing rows or creates them.
    /// That get-or-create is a database concern, so it lives here rather than in
    /// a mapper — an Application-layer mapper has no way to look up existing rows.
    /// </summary>
    /// <param name="createdByUserId">
    /// Null for scraped recipes, which deliberately have no owner and so can be
    /// edited by nobody through the API.
    /// </param>
    Task<Guid> CreateAsync(
        CreateRecipeRequest request,
        Guid? createdByUserId,
        RecipeSourceType sourceType,
        string? sourceUrl,
        CancellationToken ct);

    /// <summary>
    /// Replaces an unowned (scraped) recipe in place, for the Worker's re-promote
    /// path. Refuses anything with an owner, so it cannot be used to sidestep the
    /// ownership rule. Updating rather than delete-and-recreate keeps the recipe
    /// id stable, so favorites and links survive a re-promote.
    /// </summary>
    Task<WriteResult> ReplaceScrapedAsync(Guid id, CreateRecipeRequest request, CancellationToken ct);

    Task<WriteResult> UpdateAsync(Guid id, CreateRecipeRequest request, Guid currentUserId, CancellationToken ct);

    Task<WriteResult> DeleteAsync(Guid id, Guid currentUserId, CancellationToken ct);

    Task<Guid?> FindIdBySourceUrlAsync(string sourceUrl, CancellationToken ct);
}

/// <summary>
/// Raised when an insert loses the race to the unique index on Recipes.SourceUrl.
/// A check-then-insert cannot close that window on its own, and letting the
/// database arbitrate is better than pretending the check was atomic. Declared
/// here so Infrastructure can translate the provider's error without the
/// Application layer ever seeing an EF or Npgsql type.
/// </summary>
public class DuplicateSourceUrlException(string sourceUrl)
    : Exception($"A recipe has already been imported from {sourceUrl}.")
{
    public string SourceUrl { get; } = sourceUrl;
}

public interface ILookupRepository
{
    Task<IReadOnlyList<CuisineDto>> GetCuisinesAsync(CancellationToken ct);

    /// <param name="search">Optional substring filter for autocomplete.</param>
    Task<IReadOnlyList<IngredientDto>> SearchIngredientsAsync(string? search, int limit, CancellationToken ct);
}
