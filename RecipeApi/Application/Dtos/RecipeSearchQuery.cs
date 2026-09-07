using RecipeApi.Domain;

namespace RecipeApi.Application.Dtos;

public record RecipeSearchQuery(
    List<string>? Cuisines = null,
    List<string>? Tags = null,
    List<string>? IngredientKeywords = null,
    bool ExactIngredientMatch = false,
    DifficultyLevel? Difficulty = null,
    int? MaxTotalTimeMinutes = null,
    string? TextSearch = null,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
}
