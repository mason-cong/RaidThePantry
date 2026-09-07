using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeApi.Domain;

namespace RecipeApi.Infrastructure.Persistence.Configurations;

public class CuisineConfiguration : IEntityTypeConfiguration<Cuisine>
{
    public void Configure(EntityTypeBuilder<Cuisine> builder)
    {
        builder.ToTable("Cuisines");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(t => t.Name).IsUnique();
    }
}

public class RecipeCuisineConfiguration : IEntityTypeConfiguration<RecipeCuisine>
{
    public void Configure(EntityTypeBuilder<RecipeCuisine> builder)
    {
        builder.ToTable("RecipeCuisines");

        // Composite PK is right here — tagging a recipe "Italian" twice is meaningless.
        builder.HasKey(rc => new { rc.RecipeId, rc.CuisineId });
        builder.HasIndex(rc => rc.CuisineId);

        builder.HasOne(rc => rc.Cuisine)
            .WithMany()
            .HasForeignKey(rc => rc.CuisineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RecipeTagConfiguration : IEntityTypeConfiguration<RecipeTag>
{
    public void Configure(EntityTypeBuilder<RecipeTag> builder)
    {
        builder.ToTable("RecipeTags");
        builder.HasKey(rt => new { rt.RecipeId, rt.TagId });
        builder.HasIndex(rt => rt.TagId);

        builder.HasOne(rt => rt.Tag)
            .WithMany()
            .HasForeignKey(rt => rt.TagId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class UserFavoriteConfiguration : IEntityTypeConfiguration<UserFavorite>
{
    public void Configure(EntityTypeBuilder<UserFavorite> builder)
    {
        builder.ToTable("UserFavorites");
        builder.HasKey(f => new { f.UserId, f.RecipeId });
        builder.HasIndex(f => f.RecipeId);

        builder.HasOne(f => f.Recipe)
            .WithMany()
            .HasForeignKey(f => f.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany(u => u.Favorites)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
