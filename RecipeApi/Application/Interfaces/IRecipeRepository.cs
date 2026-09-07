using RecipeApi.Application.Dtos;

namespace RecipeApi.Application.Interfaces;

/// <summary>
/// Read operations only for now. Create/Update/Delete arrive in build-order
/// step 8, together with the ownership rule they have to enforce — declaring
/// them here before they exist would only invite NotImplementedException stubs.
/// </summary>
public interface IRecipeRepository
{
    Task<PagedResult<RecipeSummaryDto>> SearchAsync(RecipeSearchQuery query, CancellationToken ct);

    /// <param name="currentUserId">Null for guests; drives RecipeDetailDto.IsEditable.</param>
    Task<RecipeDetailDto?> GetByIdAsync(Guid id, Guid? currentUserId, CancellationToken ct);
}

public interface ILookupRepository
{
    Task<IReadOnlyList<CuisineDto>> GetCuisinesAsync(CancellationToken ct);

    /// <param name="search">Optional substring filter for autocomplete.</param>
    Task<IReadOnlyList<IngredientDto>> SearchIngredientsAsync(string? search, int limit, CancellationToken ct);
}
