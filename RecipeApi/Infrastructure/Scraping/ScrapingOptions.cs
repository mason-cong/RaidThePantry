namespace RecipeApi.Infrastructure.Scraping;

public class ScrapingOptions
{
    public const string SectionName = "Scraping";

    /// <summary>
    /// Permits fetching from 127.0.0.1, for testing against a local fixture
    /// server. Development only — leaving this on anywhere reachable turns the
    /// import endpoint back into an SSRF tool.
    /// </summary>
    public bool AllowLoopbackHosts { get; set; }

    public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024;
    public int TimeoutSeconds { get; set; } = 15;
    public int MaxRedirects { get; set; } = 5;

    /// <summary>
    /// The placeholder. Named so StartupChecks can test for "still the default"
    /// exactly, rather than sniffing for a substring like "example.com" — that
    /// rejects any real contact URL which happens to contain it, the
    /// documentation's own example domain included.
    /// </summary>
    public const string DefaultUserAgent = "RecipeFinderBot/0.1 (+https://example.com/bot)";

    /// <summary>Identifies the crawler honestly, with a contact route.</summary>
    public string UserAgent { get; set; } = DefaultUserAgent;

    /// <summary>The name robots.txt groups are matched against.</summary>
    public string UserAgentToken { get; set; } = "RecipeFinderBot";

    /// <summary>
    /// Minimum gap between bulk-crawl requests. A site's own Crawl-delay wins
    /// when it asks for more.
    /// </summary>
    public double PolitenessDelaySeconds { get; set; } = 1.0;

    /// <summary>
    /// Stamped onto every staged page at promote time. Raising it marks the
    /// existing corpus as promoted by an older transform, which is what
    /// `promote --repromote` re-runs after the normalizer changes.
    /// </summary>
    public int ParserVersion { get; set; } = 1;
}
