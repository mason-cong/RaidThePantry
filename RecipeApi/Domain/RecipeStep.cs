namespace RecipeApi.Domain;

public class RecipeStep
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RecipeId { get; set; }

    /// <summary>1-based position in the instruction list.</summary>
    public int Order { get; set; }

    public required string Instruction { get; set; }
}
