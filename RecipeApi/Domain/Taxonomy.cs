namespace RecipeApi.Domain;

public class Cuisine
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
}

public class Tag
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>"vegan", "gluten-free", "quick", ...</summary>
    public required string Name { get; set; }
}

public class RecipeCuisine
{
    public Guid RecipeId { get; set; }
    public Guid CuisineId { get; set; }
    public Cuisine Cuisine { get; set; } = null!;
}

public class RecipeTag
{
    public Guid RecipeId { get; set; }
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
