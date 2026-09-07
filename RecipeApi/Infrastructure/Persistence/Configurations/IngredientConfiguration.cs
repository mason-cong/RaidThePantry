using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeApi.Domain;

namespace RecipeApi.Infrastructure.Persistence.Configurations;

public class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> builder)
    {
        builder.ToTable("Ingredients");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Name).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Category).HasMaxLength(100);

        // Plain (deterministic, case-sensitive) unique index is correct here because
        // IIngredientNormalizer guarantees a lowercase canonical name — so casing
        // variants can never reach this column in the first place.
        //
        // Deliberately NOT a case-insensitive ICU collation: Postgres refuses LIKE
        // and ILIKE against nondeterministic collations, which would break the
        // fuzzy ingredient search this column exists to serve.
        builder.HasIndex(i => i.Name).IsUnique();

        builder.HasIndex(i => i.Category);
    }
}

public class RecipeIngredientConfiguration : IEntityTypeConfiguration<RecipeIngredient>
{
    public void Configure(EntityTypeBuilder<RecipeIngredient> builder)
    {
        builder.ToTable("RecipeIngredients");
        builder.HasKey(ri => ri.Id);
        builder.Property(ri => ri.Id).ValueGeneratedNever();

        builder.Property(ri => ri.RawText).HasMaxLength(500);
        builder.Property(ri => ri.Unit).HasMaxLength(50);
        builder.Property(ri => ri.Quantity).HasPrecision(10, 3);

        // Non-unique on both sides: the same ingredient may appear twice in one
        // recipe (flour for the dough, flour for dusting).
        builder.HasIndex(ri => new { ri.RecipeId, ri.Order });
        builder.HasIndex(ri => ri.IngredientId);

        builder.HasOne(ri => ri.Ingredient)
            .WithMany()
            .HasForeignKey(ri => ri.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);   // deleting a recipe never deletes ingredients
    }
}
