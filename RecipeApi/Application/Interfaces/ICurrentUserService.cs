namespace RecipeApi.Application.Interfaces;

/// <summary>
/// Guest access is first-class: <see cref="UserId"/> being null is a normal
/// state, not an error, and every read endpoint must work in it.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
}

public interface IIngredientNormalizer
{
    /// <summary>
    /// Strips quantities, units and prep descriptors, returning a canonical
    /// lowercase name: "2 boneless, skinless chicken breasts, diced" -> "chicken breast".
    ///
    /// The lowercase guarantee is load-bearing — the unique index on
    /// Ingredients.Name and the exact-match search path both depend on it.
    /// Implemented in build-order step 8.
    /// </summary>
    string Normalize(string rawIngredientText);
}
