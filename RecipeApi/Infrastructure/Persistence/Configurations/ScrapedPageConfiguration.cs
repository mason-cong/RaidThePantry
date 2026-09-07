using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeApi.Domain.Staging;

namespace RecipeApi.Infrastructure.Persistence.Configurations;

public class ScrapedPageConfiguration : IEntityTypeConfiguration<ScrapedPage>
{
    public void Configure(EntityTypeBuilder<ScrapedPage> builder)
    {
        // Separate schema, same DbContext and migration history: one connection and
        // one transaction for the promote step, while the prefix keeps it obvious
        // that this is not part of the served model.
        builder.ToTable("ScrapedPages", "staging");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Url).HasMaxLength(2048).IsRequired();
        builder.HasIndex(p => p.Url).IsUnique();     // makes `scrape` idempotent

        builder.Property(p => p.ExtractedJsonLd).HasColumnType("jsonb");
        builder.Property(p => p.RawHtml).HasColumnType("text");
        builder.Property(p => p.ErrorMessage).HasMaxLength(2000);

        // The promote command's working set: unpromoted rows, and rows left behind
        // by an older parser version.
        builder.HasIndex(p => new { p.Promotion, p.ParserVersion });
        builder.HasIndex(p => p.Extraction);
    }
}
