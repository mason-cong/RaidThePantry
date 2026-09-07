using RecipeApi.Domain;

namespace RecipeApi.Application.Dtos;

public record RecipeSummaryDto(
    Guid Id,
    string Title,
    string? ImageUrl,
    int TotalTimeMinutes,
    DifficultyLevel Difficulty,
    IReadOnlyList<string> Cuisines);

public record RecipeIngredientDto(
    string Name,
    decimal? Quantity,
    string? Unit,
    string? RawText);

public record RecipeDetailDto(
    Guid Id,
    string Title,
    string? Description,
    int PrepTimeMinutes,
    int CookTimeMinutes,
    int Servings,
    DifficultyLevel Difficulty,
    string? ImageUrl,
    string? SourceUrl,
    RecipeSourceType SourceType,
    // True only when the caller created this recipe. Scraped recipes have no
    // owner and are editable by nobody, so this lets the frontend hide controls
    // without reimplementing the ownership rule.
    bool IsEditable,
    IReadOnlyList<RecipeIngredientDto> Ingredients,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Cuisines,
    IReadOnlyList<string> Tags);

public record CuisineDto(Guid Id, string Name, int RecipeCount);

public record IngredientDto(Guid Id, string Name, string? Category, int RecipeCount);
