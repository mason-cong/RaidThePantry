using System.Net;

namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// How every outbound scraping client is configured, in one place for the same
/// reason <see cref="GuardedHttpHandler"/> is.
///
/// The headers here are not decoration. A bare HttpClient request — no Accept,
/// no Accept-Language, HTTP/1.1 — is distinguishable from a browser, and the
/// CDNs in front of large recipe sites refuse it with 403 while serving the
/// identical URL to curl. Looking like an ordinary client is what makes the
/// difference between a crawl that works and one that collects error rows.
///
/// This is about being legible, not about hiding: the User-Agent still names
/// this crawler and carries a contact URL, and robots.txt is still obeyed.
/// </summary>
public static class ScrapingHttpDefaults
{
    public static void Apply(HttpClient client, ScrapingOptions options, bool longTimeout = false)
    {
        client.Timeout = TimeSpan.FromSeconds(
            longTimeout ? Math.Max(options.TimeoutSeconds, 60) : options.TimeoutSeconds);

        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        // Accept-Encoding is deliberately not set here — AutomaticDecompression
        // on the handler adds it, and setting both sends the header twice.
        client.DefaultRequestVersion = HttpVersion.Version20;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    }

    /// <summary>The handler half: the SSRF guard plus transparent decompression.</summary>
    public static SocketsHttpHandler Handler(ScrapingOptions options, bool followRedirects = false)
    {
        var handler = GuardedHttpHandler.Create(options.AllowLoopbackHosts, followRedirects);
        handler.AutomaticDecompression = DecompressionMethods.All;
        return handler;
    }
}
