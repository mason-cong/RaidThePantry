using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RecipeApi.Domain;
using RecipeApi.Domain.Staging;

namespace RecipeApi.Infrastructure.Persistence;

public class RecipeDbContext(DbContextOptions<RecipeDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<Cuisine> Cuisines => Set<Cuisine>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<RecipeCuisine> RecipeCuisines => Set<RecipeCuisine>();
    public DbSet<RecipeTag> RecipeTags => Set<RecipeTag>();
    public DbSet<UserFavorite> UserFavorites => Set<UserFavorite>();

    /// <summary>Worker-only landing zone. Nothing in the API should query this.</summary>
    public DbSet<ScrapedPage> ScrapedPages => Set<ScrapedPage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity requires this before anything else — it configures the
        // AspNetUsers/AspNetRoles/... tables.
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(RecipeDbContext).Assembly);
    }
}
