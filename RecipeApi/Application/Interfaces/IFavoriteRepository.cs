using RecipeApi.Application.Dtos;

namespace RecipeApi.Application.Interfaces;

public enum FavoriteResult
{
    Added,
    AlreadyFavorited,
    RecipeNotFound
}

public interface IFavoriteRepository
{
    /// <summary>Most recently favorited first — the order a "saved" list is read in.</summary>
    Task<PagedResult<RecipeSummaryDto>> GetFavoritesAsync(
        Guid userId, int page, int pageSize, CancellationToken ct);

    Task<FavoriteResult> AddAsync(Guid userId, Guid recipeId, CancellationToken ct);

    /// <returns>True if a favorite was removed; false if there was nothing to remove.</returns>
    Task<bool> RemoveAsync(Guid userId, Guid recipeId, CancellationToken ct);
}
