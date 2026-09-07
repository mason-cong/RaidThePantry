using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;
using RecipeApi.Infrastructure.Persistence;

namespace RecipeApi.Infrastructure.Repositories;

public class RecipeRepository(RecipeDbContext context) : IRecipeRepository
{
    /// <summary>
    /// Hoisted into a field rather than written as a method call inside Select.
    /// EF Core translates expression trees, and cannot see inside an arbitrary
    /// static method — `Select(r => Mapper.ToSummary(r))` fails to translate.
    /// </summary>
    private static readonly Expression<Func<Recipe, RecipeSummaryDto>> ToSummary =
        r => new RecipeSummaryDto(
            r.Id,
            r.Title,
            r.ImageUrl,
            r.PrepTimeMinutes + r.CookTimeMinutes,
            r.Difficulty,
            r.Cuisines.Select(rc => rc.Cuisine.Name).ToList());

    public async Task<PagedResult<RecipeSummaryDto>> SearchAsync(RecipeSearchQuery query, CancellationToken ct)
    {
        var q = context.Recipes.AsNoTracking();

        // A client-side list on the OUTSIDE of the predicate translates to IN (...).
        if (query.Cuisines is { Count: > 0 })
            q = q.Where(r => r.Cuisines.Any(rc => query.Cuisines.Contains(rc.Cuisine.Name)));

        if (query.Tags is { Count: > 0 })
            q = q.Where(r => r.Tags.Any(rt => query.Tags.Contains(rt.Tag.Name)));

        // One Where per keyword, deliberately.
        //
        // The obvious formulation --
        //     q.Where(r => keywords.All(kw => r.Ingredients.Any(ri => ...)))
        // -- does not translate: `.All()` over a *client-side* collection applied
        // to a server-side navigation is not something EF can turn into SQL, and
        // it throws at runtime rather than failing to compile. Chaining one Where
        // per keyword produces the same AND-of-EXISTS the query needs.
        foreach (var keyword in query.IngredientKeywords ?? [])
        {
            // Captured per iteration; normalized to match the lowercase canonical
            // form guaranteed by IIngredientNormalizer.
            var kw = keyword.Trim().ToLowerInvariant();
            if (kw.Length == 0)
                continue;

            if (query.ExactIngredientMatch)
            {
                // Plain equality, which is a B-tree index hit on Ingredients.Name.
                q = q.Where(r => r.Ingredients.Any(ri => ri.Ingredient.Name == kw));
            }
            else
            {
                var pattern = $"%{EscapeLike(kw)}%";
                q = q.Where(r => r.Ingredients.Any(ri =>
                    EF.Functions.ILike(ri.Ingredient.Name, pattern, LikeEscape)));
            }
        }

        if (query.Difficulty.HasValue)
            q = q.Where(r => r.Difficulty == query.Difficulty.Value);

        if (query.MaxTotalTimeMinutes is int maxMinutes)
            q = q.Where(r => r.PrepTimeMinutes + r.CookTimeMinutes <= maxMinutes);

        if (!string.IsNullOrWhiteSpace(query.TextSearch))
        {
            var titlePattern = $"%{EscapeLike(query.TextSearch.Trim())}%";
            q = q.Where(r => EF.Functions.ILike(r.Title, titlePattern, LikeEscape));
        }

        // Counted before ordering: ORDER BY contributes nothing to a COUNT and
        // Postgres would only discard it.
        var totalCount = await q.CountAsync(ct);

        var items = await ApplySort(q, query.SortBy)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToSummary)
            .ToListAsync(ct);

        return new PagedResult<RecipeSummaryDto>(items, totalCount, query.Page, query.PageSize);
    }

    public Task<RecipeDetailDto?> GetByIdAsync(Guid id, Guid? currentUserId, CancellationToken ct)
    {
        // Guid.Empty is never a real user id (ids are UUIDv7), so it is a safe
        // stand-in for "no caller" and keeps the comparison translatable.
        var ownerProbe = currentUserId ?? Guid.Empty;

        return context.Recipes
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new RecipeDetailDto(
                r.Id,
                r.Title,
                r.Description,
                r.PrepTimeMinutes,
                r.CookTimeMinutes,
                r.Servings,
                r.Difficulty,
                r.ImageUrl,
                r.SourceUrl,
                r.SourceType,
                r.CreatedByUserId.HasValue && r.CreatedByUserId.Value == ownerProbe,
                r.Ingredients
                    .OrderBy(ri => ri.Order)
                    .Select(ri => new RecipeIngredientDto(
                        ri.Ingredient.Name, ri.Quantity, ri.Unit, ri.RawText))
                    .ToList(),
                r.Steps.OrderBy(s => s.Order).Select(s => s.Instruction).ToList(),
                r.Cuisines.Select(rc => rc.Cuisine.Name).ToList(),
                r.Tags.Select(rt => rt.Tag.Name).ToList()))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Every branch carries an Id tiebreaker. Without one, rows tying on the sort
    /// key have no defined relative order, so the database is free to return them
    /// differently between the query for page 1 and the query for page 2 — which
    /// shows up as a row appearing twice, or vanishing entirely.
    /// </summary>
    private static IQueryable<Recipe> ApplySort(IQueryable<Recipe> q, string? sortBy) => sortBy switch
    {
        "time" => q.OrderBy(r => r.PrepTimeMinutes + r.CookTimeMinutes).ThenBy(r => r.Id),
        "title" => q.OrderBy(r => r.Title).ThenBy(r => r.Id),
        _ => q.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
    };

    internal const string LikeEscape = "\\";

    /// <summary>
    /// Neutralizes LIKE metacharacters in user input. Without this a search for
    /// "2%" matches every ingredient containing "2" followed by anything, and
    /// "half_and" would match "half-and".
    /// </summary>
    internal static string EscapeLike(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
