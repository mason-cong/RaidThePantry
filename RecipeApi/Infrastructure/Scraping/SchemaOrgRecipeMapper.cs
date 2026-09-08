using RecipeApi.Application.Dtos;
using RecipeApi.Domain;

namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// Turns an extracted schema.org recipe into a create request. Shared so the
/// API's import and the Worker's promote produce identical recipes from
/// identical input — the whole point of staging the raw payload is that the
/// transform can be replayed, which only holds if there is one transform.
/// </summary>
public static class SchemaOrgRecipeMapper
{
    public static CreateRecipeRequest ToCreateRequest(SchemaOrgRecipe source)
    {
        var prep = source.PrepTimeMinutes ?? 0;
        var cook = source.CookTimeMinutes ?? 0;

        // Many sites publish only totalTime. Attribute the remainder to cooking
        // rather than dropping it, so search on total time stays meaningful.
        if (source.TotalTimeMinutes is int total && total > 0)
        {
            if (prep == 0 && cook == 0)
                cook = total;
            else if (cook == 0 && total > prep)
                cook = total - prep;
        }

        var ingredients = source.Ingredients
            .Take(100)
            .Select(line => new CreateIngredientRequest(
                Truncate(line, 200),
                Quantity: null,   // stripped by the normalizer; not modelled separately yet
                Unit: null,
                RawText: Truncate(line, 500)))
            .ToList();

        return new CreateRecipeRequest(
            Title: Truncate(source.Name!, 300),
            Description: source.Description is null ? null : Truncate(source.Description, 4000),
            PrepTimeMinutes: Math.Clamp(prep, 0, 10080),
            CookTimeMinutes: Math.Clamp(cook, 0, 10080),
            Servings: Math.Clamp(source.Servings ?? 4, 1, 1000),
            // schema.org has no difficulty field, and inferring one from step
            // count would be inventing data. Medium is the honest default.
            Difficulty: DifficultyLevel.Medium,
            ImageUrl: source.ImageUrl is null ? null : Truncate(source.ImageUrl, 2048),
            Cuisines: source.Cuisines.Take(5).Select(c => Truncate(c, 100)).ToList(),
            Tags: source.Keywords.Take(12).Select(k => Truncate(k, 100)).ToList(),
            Ingredients: ingredients,
            Steps: source.Steps.Take(100).Select(s => Truncate(s, 4000)).ToList());
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
