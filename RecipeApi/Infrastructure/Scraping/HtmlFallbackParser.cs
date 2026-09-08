namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// Last-resort CSS-pattern scraping for pages carrying no JSON-LD. Stubbed in v1
/// by design: the selectors differ per site, so writing them without a corpus of
/// failing pages is guesswork. staging.ScrapedPage keeps the raw HTML of exactly
/// the pages where extraction failed, which is the corpus to build this from.
/// </summary>
public class HtmlFallbackParser
{
    public SchemaOrgRecipe? Extract(string html) => null;
}
