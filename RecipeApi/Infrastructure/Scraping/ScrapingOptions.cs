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

    /// <summary>Identifies the crawler honestly, with a contact route.</summary>
    public string UserAgent { get; set; } = "RecipeFinderBot/0.1 (+https://example.com/bot)";
}
