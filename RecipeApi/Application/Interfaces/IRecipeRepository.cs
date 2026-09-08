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
    Task<Guid> CreateAsync(
        CreateRecipeRequest request,
        Guid createdByUserId,
        RecipeSourceType sourceType,
        string? sourceUrl,
        CancellationToken ct);

    Task<WriteResult> UpdateAsync(Guid id, CreateRecipeRequest request, Guid currentUserId, CancellationToken ct);

    Task<WriteResult> DeleteAsync(Guid id, Guid currentUserId, CancellationToken ct);
}

public interface ILookupRepository
{
    Task<IReadOnlyList<CuisineDto>> GetCuisinesAsync(CancellationToken ct);

    /// <param name="search">Optional substring filter for autocomplete.</param>
    Task<IReadOnlyList<IngredientDto>> SearchIngredientsAsync(string? search, int limit, CancellationToken ct);
}
