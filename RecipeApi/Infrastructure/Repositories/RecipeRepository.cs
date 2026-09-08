using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;
using RecipeApi.Infrastructure.Persistence;

namespace RecipeApi.Infrastructure.Repositories;

public class RecipeRepository(RecipeDbContext context, IIngredientNormalizer normalizer) : IRecipeRepository
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

    public async Task<Guid> CreateAsync(
        CreateRecipeRequest request,
        Guid createdByUserId,
        RecipeSourceType sourceType,
        string? sourceUrl,
        CancellationToken ct)
    {
        var recipe = new Recipe
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            PrepTimeMinutes = request.PrepTimeMinutes,
            CookTimeMinutes = request.CookTimeMinutes,
            Servings = request.Servings,
            Difficulty = request.Difficulty,
            ImageUrl = request.ImageUrl?.Trim(),
            SourceType = sourceType,
            SourceUrl = sourceUrl,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await PopulateChildrenAsync(recipe, request, ct);

        context.Recipes.Add(recipe);
        await context.SaveChangesAsync(ct);

        return recipe.Id;
    }

    public async Task<WriteResult> UpdateAsync(
        Guid id, CreateRecipeRequest request, Guid currentUserId, CancellationToken ct)
    {
        var recipe = await context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.Cuisines)
            .Include(r => r.Tags)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (recipe is null)
            return WriteResult.NotFound;

        if (!CanModify(recipe, currentUserId))
            return WriteResult.Forbidden;

        recipe.Title = request.Title.Trim();
        recipe.Description = request.Description?.Trim();
        recipe.PrepTimeMinutes = request.PrepTimeMinutes;
        recipe.CookTimeMinutes = request.CookTimeMinutes;
        recipe.Servings = request.Servings;
        recipe.Difficulty = request.Difficulty;
        recipe.ImageUrl = request.ImageUrl?.Trim();
        recipe.UpdatedAt = DateTimeOffset.UtcNow;

        // Children are replaced wholesale rather than diffed. Every child is
        // owned by exactly one recipe and carries no identity a client refers to,
        // so a diff would buy nothing but a chance to get it wrong. Clearing a
        // required relationship marks the orphans deleted.
        recipe.Ingredients.Clear();
        recipe.Steps.Clear();
        recipe.Cuisines.Clear();
        recipe.Tags.Clear();

        await PopulateChildrenAsync(recipe, request, ct);
        await context.SaveChangesAsync(ct);

        return WriteResult.Success;
    }

    public async Task<WriteResult> DeleteAsync(Guid id, Guid currentUserId, CancellationToken ct)
    {
        var recipe = await context.Recipes.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (recipe is null)
            return WriteResult.NotFound;

        if (!CanModify(recipe, currentUserId))
            return WriteResult.Forbidden;

        // Ingredients, steps, cuisine and tag links go with it by DB cascade;
        // the Ingredient/Cuisine/Tag rows themselves are shared and stay put.
        context.Recipes.Remove(recipe);
        await context.SaveChangesAsync(ct);

        return WriteResult.Success;
    }

    /// <summary>
    /// A null CreatedByUserId means scraped content, which belongs to nobody and
    /// is therefore editable by nobody — not by the first person to claim it.
    /// </summary>
    private static bool CanModify(Recipe recipe, Guid currentUserId) =>
        recipe.CreatedByUserId is Guid owner && owner == currentUserId;

    private async Task PopulateChildrenAsync(Recipe recipe, CreateRecipeRequest request, CancellationToken ct)
    {
        // Normalize first, so the get-or-create lookup keys match what is stored.
        var normalized = request.Ingredients
            .Select(i => (Request: i, Name: normalizer.Normalize(i.Name)))
            .Where(x => x.Name.Length > 0)
            .ToList();

        var ingredients = await ResolveIngredientsAsync(normalized.Select(x => x.Name), ct);

        var order = 0;
        foreach (var (item, name) in normalized)
        {
            recipe.Ingredients.Add(new RecipeIngredient
            {
                Ingredient = ingredients[name],
                // Keep what the user actually typed; the normalized name is lossy
                // by design and the original is what a reader wants to see.
                RawText = string.IsNullOrWhiteSpace(item.RawText) ? item.Name.Trim() : item.RawText.Trim(),
                Quantity = item.Quantity,
                Unit = item.Unit?.Trim(),
                Order = order++
            });
        }

        var stepNumber = 1;
        foreach (var instruction in request.Steps.Where(s => !string.IsNullOrWhiteSpace(s)))
            recipe.Steps.Add(new RecipeStep { Order = stepNumber++, Instruction = instruction.Trim() });

        foreach (var cuisine in await ResolveCuisinesAsync(request.Cuisines, ct))
            recipe.Cuisines.Add(new RecipeCuisine { Cuisine = cuisine });

        foreach (var tag in await ResolveTagsAsync(request.Tags, ct))
            recipe.Tags.Add(new RecipeTag { Tag = tag });
    }

    // The three Resolve* methods below are the get-or-create the plan had no home
    // for. It cannot live in an Application-layer mapper: turning "chicken breast"
    // into *the existing* Ingredient row requires a database lookup.
    //
    // Concurrent creation of the same new name will violate the unique index on
    // Name and fail the request. At this write volume that is rare enough to leave
    // to the caller's retry; a race-proof version needs an upsert.
    private async Task<Dictionary<string, Ingredient>> ResolveIngredientsAsync(
        IEnumerable<string> names, CancellationToken ct)
    {
        var distinct = names.Distinct(StringComparer.Ordinal).ToList();

        // Normalizer output is already lowercase, so an exact match is correct
        // here and uses the unique B-tree index.
        var resolved = await context.Ingredients
            .Where(i => distinct.Contains(i.Name))
            .ToDictionaryAsync(i => i.Name, ct);

        foreach (var name in distinct.Where(n => !resolved.ContainsKey(n)))
        {
            var created = new Ingredient { Name = name };
            context.Ingredients.Add(created);
            resolved[name] = created;
        }

        return resolved;
    }

    private async Task<List<Cuisine>> ResolveCuisinesAsync(List<string>? names, CancellationToken ct)
    {
        var distinct = CleanNames(names);
        if (distinct.Count == 0)
            return [];

        // Cuisine and tag names are user-facing display text, not normalizer
        // output, so "italian" must find the existing "Italian" rather than
        // creating a near-duplicate. Matching is case-insensitive; the casing the
        // first writer used is what gets stored.
        var lowered = distinct.Select(n => n.ToLowerInvariant()).ToList();

        var existing = await context.Cuisines
            .Where(c => lowered.Contains(c.Name.ToLower()))
            .ToListAsync(ct);

        var byLower = existing.ToDictionary(c => c.Name.ToLowerInvariant(), StringComparer.Ordinal);
        var result = new List<Cuisine>();

        foreach (var name in distinct)
        {
            var key = name.ToLowerInvariant();
            if (!byLower.TryGetValue(key, out var cuisine))
            {
                cuisine = new Cuisine { Name = name };
                context.Cuisines.Add(cuisine);
                byLower[key] = cuisine;
            }
            result.Add(cuisine);
        }

        return result;
    }

    private async Task<List<Tag>> ResolveTagsAsync(List<string>? names, CancellationToken ct)
    {
        var distinct = CleanNames(names);
        if (distinct.Count == 0)
            return [];

        var lowered = distinct.Select(n => n.ToLowerInvariant()).ToList();

        var existing = await context.Tags
            .Where(t => lowered.Contains(t.Name.ToLower()))
            .ToListAsync(ct);

        var byLower = existing.ToDictionary(t => t.Name.ToLowerInvariant(), StringComparer.Ordinal);
        var result = new List<Tag>();

        foreach (var name in distinct)
        {
            var key = name.ToLowerInvariant();
            if (!byLower.TryGetValue(key, out var tag))
            {
                tag = new Tag { Name = name };
                context.Tags.Add(tag);
                byLower[key] = tag;
            }
            result.Add(tag);
        }

        return result;
    }

    private static List<string> CleanNames(List<string>? names) =>
        names is null
            ? []
            : names
                .Select(n => n.Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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
