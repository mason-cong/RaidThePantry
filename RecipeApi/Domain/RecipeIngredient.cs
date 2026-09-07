namespace RecipeApi.Domain;

public class RecipeIngredient
{
    // Surrogate key rather than a composite (RecipeId, IngredientId) PK: a recipe
    // may legitimately list the same ingredient twice — flour for the dough and
    // flour for dusting — and scraped recipes hit that constantly.
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RecipeId { get; set; }
    public Guid IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;

    /// <summary>The unparsed source line, e.g. "2 boneless chicken breasts, diced".</summary>
    public string? RawText { get; set; }

    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }

    /// <summary>Preserves the order ingredients were listed in at the source.</summary>
    public int Order { get; set; }
}
