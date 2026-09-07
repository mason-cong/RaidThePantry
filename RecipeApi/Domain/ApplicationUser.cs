using Microsoft.AspNetCore.Identity;

namespace RecipeApi.Domain;

/// <summary>
/// Accounts exist only to contribute recipes and persist favorites — all read
/// endpoints stay anonymous, so a user is never required to browse.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; }
    public List<UserFavorite> Favorites { get; set; } = [];
}

public class UserFavorite
{
    // Composite PK (UserId, RecipeId) — favoriting the same recipe twice is
    // genuinely meaningless, unlike listing the same ingredient twice.
    public Guid UserId { get; set; }
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public DateTimeOffset FavoritedAt { get; set; }
}
