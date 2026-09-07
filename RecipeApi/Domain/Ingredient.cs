namespace RecipeApi.Domain;

/// <summary>
/// A canonical ingredient, deduplicated across every recipe. <see cref="Name"/>
/// is the normalized form produced by IIngredientNormalizer ("chicken breast"),
/// never the raw source line — that lives on RecipeIngredient.RawText.
/// </summary>
public class Ingredient
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>"protein", "vegetable", "dairy", ... Unset until categorization exists.</summary>
    public string? Category { get; set; }
}
