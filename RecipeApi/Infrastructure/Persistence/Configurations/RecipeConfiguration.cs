using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeApi.Domain;

namespace RecipeApi.Infrastructure.Persistence.Configurations;

public class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> builder)
    {
        builder.ToTable("Recipes");
        builder.HasKey(r => r.Id);

        // Ids are generated client-side as UUIDv7 (time-ordered, so inserts stay
        // at the right edge of the index instead of scattering like UUIDv4).
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Title).HasMaxLength(300).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(4000);
        builder.Property(r => r.ImageUrl).HasMaxLength(2048);
        builder.Property(r => r.SourceUrl).HasMaxLength(2048);

        // Prevents the same page being imported twice. Filtered, because manually
        // created recipes all have a NULL SourceUrl and would otherwise collide.
        builder.HasIndex(r => r.SourceUrl)
            .IsUnique()
            .HasFilter("\"SourceUrl\" IS NOT NULL");

        builder.HasIndex(r => r.CreatedAt);
        builder.HasIndex(r => r.CreatedByUserId);
        builder.HasIndex(r => r.SourceType);

        builder.HasMany(r => r.Ingredients)
            .WithOne()
            .HasForeignKey(ri => ri.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Steps)
            .WithOne()
            .HasForeignKey(s => s.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Cuisines)
            .WithOne()
            .HasForeignKey(rc => rc.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Tags)
            .WithOne()
            .HasForeignKey(rt => rt.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RecipeStepConfiguration : IEntityTypeConfiguration<RecipeStep>
{
    public void Configure(EntityTypeBuilder<RecipeStep> builder)
    {
        builder.ToTable("RecipeSteps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Instruction).HasMaxLength(4000).IsRequired();

        builder.HasIndex(s => new { s.RecipeId, s.Order });
    }
}
