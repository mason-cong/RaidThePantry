using Microsoft.EntityFrameworkCore;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Infrastructure.Persistence;

namespace RecipeApi.Infrastructure.Repositories;

/// <summary>Backs the filter-UI and autocomplete endpoints.</summary>
public class LookupRepository(RecipeDbContext context) : ILookupRepository
{
    public async Task<IReadOnlyList<CuisineDto>> GetCuisinesAsync(CancellationToken ct) =>
        await context.Cuisines
            .AsNoTracking()
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
