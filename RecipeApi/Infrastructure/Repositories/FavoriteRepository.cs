using Microsoft.EntityFrameworkCore;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;
using RecipeApi.Infrastructure.Persistence;

namespace RecipeApi.Infrastructure.Repositories;

public class FavoriteRepository(RecipeDbContext context) : IFavoriteRepository
{
    public async Task<PagedResult<RecipeSummaryDto>> GetFavoritesAsync(
        Guid userId, int page, int pageSize, CancellationToken ct)
    {
        var (safePage, safePageSize) = Paging.Normalize(page, pageSize);

        // Queried from UserFavorites rather than Recipes: the ordering the caller
        // wants is by when *they* saved it, which only exists on this side.
        var q = context.UserFavorites
            .AsNoTracking()
            .Where(f => f.UserId == userId);

        var totalCount = await q.CountAsync(ct);

        var items = await q
            // RecipeId tiebreaker for the same reason every other sort has one:
            // rows tying on FavoritedAt have no defined order, so a page boundary
            // falling inside a tie can duplicate or drop one.
            .OrderByDescending(f => f.FavoritedAt)
            .ThenBy(f => f.RecipeId)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(f => new RecipeSummaryDto(
                f.Recipe.Id,
                f.Recipe.Title,
                f.Recipe.ImageUrl,
                f.Recipe.PrepTimeMinutes + f.Recipe.CookTimeMinutes,
                f.Recipe.Difficulty,
                f.Recipe.Cuisines.OrderBy(rc => rc.Cuisine.Name).Select(rc => rc.Cuisine.Name).ToList(),
                // Everything in this list is favorited by definition — no subquery.
                true))
            .ToListAsync(ct);

        return new PagedResult<RecipeSummaryDto>(items, totalCount, safePage, safePageSize);
    }

    public async Task<FavoriteResult> AddAsync(Guid userId, Guid recipeId, CancellationToken ct)
    {
        // Checked explicitly so a bad id is a 404 rather than a foreign-key
        // violation surfacing as a 500.
        if (!await context.Recipes.AnyAsync(r => r.Id == recipeId, ct))
            return FavoriteResult.RecipeNotFound;

        if (await context.UserFavorites.AnyAsync(f => f.UserId == userId && f.RecipeId == recipeId, ct))
            return FavoriteResult.AlreadyFavorited;

        context.UserFavorites.Add(new UserFavorite
        {
            UserId = userId,
            RecipeId = recipeId,
            FavoritedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            // Two rapid clicks racing between the check above and this insert.
            // The composite primary key settles it; the end state is what the
            // caller wanted either way.
            context.ChangeTracker.Clear();
            return FavoriteResult.AlreadyFavorited;
        }

        return FavoriteResult.Added;
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid recipeId, CancellationToken ct)
    {
        var removed = await context.UserFavorites
            .Where(f => f.UserId == userId && f.RecipeId == recipeId)
            .ExecuteDeleteAsync(ct);

        return removed > 0;
    }
}
