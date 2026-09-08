using System.ComponentModel.DataAnnotations;
using RecipeApi.Domain;

namespace RecipeApi.Application.Dtos;

// As with the auth DTOs, validation attributes sit on the primary-constructor
// parameters with no [property:] target.
public record CreateRecipeRequest(
    [Required, MaxLength(300)] string Title,
    [MaxLength(4000)] string? Description,
    [Range(0, 10080)] int PrepTimeMinutes,
    [Range(0, 10080)] int CookTimeMinutes,
    [Range(1, 1000)] int Servings,
    [EnumDataType(typeof(DifficultyLevel))] DifficultyLevel Difficulty,
    [MaxLength(2048)] string? ImageUrl,
    List<string>? Cuisines,
    List<string>? Tags,
    [Required, MinLength(1)] List<CreateIngredientRequest> Ingredients,
    [Required, MinLength(1)] List<string> Steps);

public record CreateIngredientRequest(
    /// Free text as typed. Passed through IIngredientNormalizer to get the
    /// canonical name; the original is preserved as RawText.
    [Required, MaxLength(200)] string Name,
    decimal? Quantity,
    [MaxLength(50)] string? Unit,
    [MaxLength(500)] string? RawText);
