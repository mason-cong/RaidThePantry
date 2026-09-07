namespace RecipeApi.Domain.Staging;

/// <summary>
/// Landing zone for the Worker's bulk crawl, in the `staging` Postgres schema.
/// The API never reads this table.
///
/// Fetching is expensive and rate-limited; transforming is cheap and will change
/// (IngredientNormalizer v1 regex -> v2 lookup). Keeping the raw payload here
/// means a parser improvement replays as a local query instead of a re-crawl.
/// </summary>
public class ScrapedPage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Unique — this is what makes re-running `scrape` idempotent.</summary>
    public required string Url { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
    public int? HttpStatus { get; set; }

    /// <summary>
    /// The schema.org/Recipe JSON-LD block, stored as jsonb. A few KB, and all
    /// that is needed to replay the transform.
    /// </summary>
    public string? ExtractedJsonLd { get; set; }

    /// <summary>
    /// Stored only when extraction failed. Full HTML is 100-500KB/page, so
    /// keeping it for every page would run to gigabytes; the failure set is
    /// exactly the corpus worth iterating HtmlFallbackParser against.
    /// </summary>
    public string? RawHtml { get; set; }

    public ExtractionStatus Extraction { get; set; }
    public PromotionStatus Promotion { get; set; }

    /// <summary>Set once this page has been turned into a Recipe row.</summary>
    public Guid? PromotedRecipeId { get; set; }

    /// <summary>Bump the current version to re-promote the whole corpus.</summary>
    public int ParserVersion { get; set; }

    public string? ErrorMessage { get; set; }
}

public enum ExtractionStatus
{
    Pending = 0,
    Extracted = 1,
    NoRecipeFound = 2,
    FetchFailed = 3
}

public enum PromotionStatus
{
    NotPromoted = 0,
    Promoted = 1,
    Rejected = 2
}
