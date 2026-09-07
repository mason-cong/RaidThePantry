using RecipeApi.Application.Dtos;
using RecipeApi.Domain;

namespace RecipeApi.Controllers;

/// <summary>
/// Flat query-string shape for GET /api/recipes, mapped to the internal
/// <see cref="RecipeSearchQuery"/>. Repeated keys and comma-separated values are
/// both accepted: ?cuisines=Italian&amp;cuisines=Thai and ?cuisines=Italian,Thai.
/// </summary>
public class RecipeSearchRequestParams
{
    public List<string>? Cuisines { get; set; }
    public List<string>? Tags { get; set; }

    /// <summary>Ingredient keywords. All of them must be present (AND, not OR).</summary>
    public List<string>? Ingredients { get; set; }

    /// <summary>When true, match the whole canonical name instead of a substring.</summary>
    public bool ExactIngredientMatch { get; set; }

    public DifficultyLevel? Difficulty { get; set; }
    public int? MaxTotalTimeMinutes { get; set; }

    /// <summary>Substring match on the recipe title.</summary>
    public string? Search { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>"time", "title", or omitted for newest first.</summary>
    public string? SortBy { get; set; }

    public RecipeSearchQuery ToSearchQuery() => new(
        Cuisines: Split(Cuisines),
        Tags: Split(Tags),
        IngredientKeywords: Split(Ingredients),
        ExactIngredientMatch: ExactIngredientMatch,
        Difficulty: Difficulty,
        MaxTotalTimeMinutes: MaxTotalTimeMinutes,
        TextSearch: Search,
        Page: Page,
        PageSize: PageSize,
        SortBy: SortBy);

    private static List<string>? Split(List<string>? values)
    {
        if (values is null || values.Count == 0)
            return null;

        var result = values
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(v => v.Length > 0)
            .ToList();

        return result.Count == 0 ? null : result;
    }
}
