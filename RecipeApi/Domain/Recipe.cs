namespace RecipeApi.Domain;

public class Recipe
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Title { get; set; }
    public string? Description { get; set; }
    public int PrepTimeMinutes { get; set; }
    public int CookTimeMinutes { get; set; }
    public int Servings { get; set; }
    public DifficultyLevel Difficulty { get; set; }
    public string? ImageUrl { get; set; }

    public RecipeSourceType SourceType { get; set; }
    public string? SourceUrl { get; set; }

    /// <summary>
    /// Null for scraped recipes, which are read-only through the API — nobody
    /// can edit or delete them. Set for anything a signed-in user created.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    // DateTimeOffset rather than DateTime: Npgsql maps DateTime to
    // `timestamp with time zone` and throws on any value whose Kind is not Utc.
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<RecipeIngredient> Ingredients { get; set; } = [];
    public List<RecipeCuisine> Cuisines { get; set; } = [];
    public List<RecipeTag> Tags { get; set; } = [];
    public List<RecipeStep> Steps { get; set; } = [];
}
