namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// The subset of schema.org/Recipe this importer uses, already flattened.
///
/// Not deserialized directly from JSON: nearly every field in the wild is
/// polymorphic — "image" may be a string, an object with a url, or an array of
/// either; "recipeInstructions" may be a string of HTML, an array of strings, an
/// array of HowToStep, or an array of HowToSection each wrapping more steps.
/// JsonLdRecipeExtractor does the flattening and fills this in.
/// </summary>
public class SchemaOrgRecipe
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int? PrepTimeMinutes { get; set; }
    public int? CookTimeMinutes { get; set; }
    public int? TotalTimeMinutes { get; set; }
    public int? Servings { get; set; }

    public List<string> Ingredients { get; set; } = [];
    public List<string> Steps { get; set; } = [];
    public List<string> Cuisines { get; set; } = [];
    public List<string> Keywords { get; set; } = [];

    /// <summary>A recipe with no ingredients or no steps is not usable, whatever else it has.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Name) && Ingredients.Count > 0 && Steps.Count > 0;
}
