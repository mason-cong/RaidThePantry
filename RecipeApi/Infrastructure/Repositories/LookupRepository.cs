using Microsoft.EntityFrameworkCore;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Infrastructure.Persistence;

namespace RecipeApi.Infrastructure.Repositories;

/// <summary>Backs the filter-UI and autocomplete endpoints.</summary>
public class LookupRepository(RecipeDbContext context) : ILookupRepository
{
    /// <summary>
    /// Only cuisines that still have recipes.
    ///
    /// Rows outlive their last recipe — a recipe deleted, or re-promoted under a
    /// normalizer that now files "Italian Cuisine" as "Italian" — and a filter
    /// offering a choice that returns nothing is worse than not offering it.
    /// The rows are left in place rather than deleted, since they cost nothing
    /// and re-attach the moment something uses them again.
    /// </summary>
    public async Task<IReadOnlyList<CuisineDto>> GetCuisinesAsync(CancellationToken ct) =>
        await context.Cuisines
            .AsNoTracking()
            // Filtered before the projection, not after. `Where` on the projected
            // CuisineDto's RecipeCount does not translate — EF cannot map a
            // record's constructor argument back to the subquery that produced
            // it — and the endpoint 500s at runtime rather than failing to build.
            .Where(c => context.RecipeCuisines.Any(rc => rc.CuisineId == c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new CuisineDto(
                c.Id,
                c.Name,
                context.RecipeCuisines.Count(rc => rc.CuisineId == c.Id)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<IngredientDto>> SearchIngredientsAsync(
        string? search, int limit, CancellationToken ct)
    {
        var q = context.Ingredients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Same escaping as the recipe search: an unescaped "%" here would
            // match every ingredient rather than the handful containing it.
            var pattern = $"%{RecipeRepository.EscapeLike(search.Trim())}%";
            q = q.Where(i => EF.Functions.ILike(i.Name, pattern, RecipeRepository.LikeEscape));
        }

        return await q
            .OrderBy(i => i.Name)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(i => new IngredientDto(
                i.Id,
                i.Name,
                i.Category,
                context.RecipeIngredients.Count(ri => ri.IngredientId == i.Id)))
            .ToListAsync(ct);
    }
}
