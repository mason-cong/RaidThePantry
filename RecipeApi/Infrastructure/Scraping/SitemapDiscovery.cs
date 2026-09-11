using System.IO.Compression;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RecipeApi.Infrastructure.Scraping;

public record DiscoveryRequest(
    /// <summary>Cap on URLs returned. 0 means no cap — see the note on Limit below.</summary>
    int Limit = 100,

    /// <summary>
    /// Case-insensitive substrings the URL must contain — <em>all</em> of them.
    ///
    /// Substrings rather than a regex because the useful filter is nearly always
    /// a path fragment, and a wrong regex fails in ways that are hard to see.
    /// More than one because a single fragment is often not selective enough:
    /// delish.com publishes recipes at "/cooking/recipe-ideas/…-recipe/" and
    /// video pages with no recipe markup at "/videos/…-recipe/", so it takes
    /// both "/recipe-ideas/" and "-recipe/" to pick out the real ones.
    /// </summary>
    IReadOnlyList<string>? Match = null,

    /// <summary>Ceiling on sitemap documents fetched, so an index of indexes cannot run away.</summary>
    int MaxSitemaps = 25);

public record DiscoveryResult(
    IReadOnlyList<string> Urls,
    int SitemapsFetched,
    int Considered,
    int RejectedByRobots)
{
    public override string ToString() =>
        $"{Urls.Count} url(s) from {SitemapsFetched} sitemap(s); " +
        $"{Considered} considered, {RejectedByRobots} disallowed by robots.txt";
}

/// <summary>
/// Finds pages worth scraping by reading the site's own sitemaps.
///
/// The alternative — fetching a homepage and following links — means guessing
/// which links are recipes, crawling pagination and tag pages that lead
/// nowhere, and issuing far more requests to find the same URLs. A sitemap is
/// the publisher stating what it wants indexed, in a format meant to be read by
/// exactly this kind of program.
///
/// Everything here is bounded. Recipe sitemaps are genuinely enormous —
/// allrecipes.com lists over 13,000 recipe URLs in one of four child sitemaps —
/// so an unbounded discover would hand `scrape` a list representing days of
/// requests against somebody else's origin. The default cap is the feature.
/// </summary>
public class SitemapDiscovery(
    HttpClient http,
    RobotsTxtChecker robots,
    IOptions<ScrapingOptions> options,
    ILogger<SitemapDiscovery> logger)
{
    private readonly ScrapingOptions _options = options.Value;

    /// <summary>A sitemap far larger than any legitimate one; stops a hostile or broken response.</summary>
    private const int MaxSitemapBytes = 64 * 1024 * 1024;

    public async Task<DiscoveryResult> DiscoverAsync(Uri site, DiscoveryRequest request, CancellationToken ct)
    {
        if (!await FetchableUrl.IsHostAllowedAsync(site, _options.AllowLoopbackHosts, ct))
            throw new InvalidOperationException($"{site.Host} is not a permitted host.");

        var rules = await robots.GetRulesAsync(site, ct);

        // Falling back to /sitemap.xml is a convention, not a standard, so it is
        // only tried when the site declares nothing.
        var roots = rules.Sitemaps.Count > 0
            ? rules.Sitemaps
            : [new Uri(site, "/sitemap.xml").ToString()];

        logger.LogInformation("Sitemaps for {Host}: {Count} declared", site.Host, rules.Sitemaps.Count);

        var pending = new Queue<string>(roots);
        var seenSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = new List<string>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int fetched = 0, considered = 0, rejectedByRobots = 0;
        var unlimited = request.Limit <= 0;

        while (pending.Count > 0 && fetched < request.MaxSitemaps && (unlimited || urls.Count < request.Limit))
        {
            ct.ThrowIfCancellationRequested();

            var current = pending.Dequeue();
            if (!seenSitemaps.Add(current))
                continue;

            // Same-origin only. A sitemap is allowed to point anywhere, and
            // following it off-site would silently widen the crawl past the host
            // whose robots.txt was consulted.
            if (!Uri.TryCreate(current, UriKind.Absolute, out var sitemapUri) ||
                !string.Equals(sitemapUri.Host, site.Host, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Ignoring off-site sitemap {Url}", current);
                continue;
            }

            string xml;
            try
            {
                xml = await ReadSitemapAsync(sitemapUri, ct);
                fetched++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                // One unreadable sitemap must not abandon the others.
                logger.LogWarning("Could not read sitemap {Url}: {Message}", current, ex.Message);
                continue;
            }

            foreach (var (loc, isIndexEntry) in ReadLocations(xml, ct))
            {
                if (isIndexEntry)
                {
                    pending.Enqueue(loc);
                    continue;
                }

                considered++;

                if (!Uri.TryCreate(loc, UriKind.Absolute, out var pageUri) ||
                    !string.Equals(pageUri.Host, site.Host, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (request.Match is { Count: > 0 } patterns &&
                    !patterns.All(p => loc.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // The same rules the crawl itself will apply, enforced here too
                // so a disallowed URL never reaches the list in the first place.
                if (!rules.Allows(pageUri.PathAndQuery))
                {
                    rejectedByRobots++;
                    continue;
                }

                if (seenUrls.Add(pageUri.ToString()))
                    urls.Add(pageUri.ToString());

                if (!unlimited && urls.Count >= request.Limit)
                    break;
            }

            // Between sitemap documents as well as between pages. These are
            // large files and the politeness budget is the site's, not ours.
            if (pending.Count > 0 && (unlimited || urls.Count < request.Limit))
                await Task.Delay(TimeSpan.FromSeconds(_options.PolitenessDelaySeconds), ct);
        }

        return new DiscoveryResult(urls, fetched, considered, rejectedByRobots);
    }

    /// <summary>
    /// Streams the document rather than loading it, so a sitemap with 20,000
    /// entries costs only as many as are actually wanted.
    /// </summary>
    private static IEnumerable<(string Loc, bool IsIndexEntry)> ReadLocations(string xml, CancellationToken ct)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,   // no external entities from a remote document
            XmlResolver = null,
            IgnoreWhitespace = true,
            IgnoreComments = true
        });

        var insideSitemapIndex = false;

        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName == "sitemapindex")
                    insideSitemapIndex = true;
                else if (reader.LocalName == "loc")
                {
                    var value = reader.ReadElementContentAsString().Trim();
                    if (value.Length > 0)
                        yield return (value, insideSitemapIndex);
                }
            }
        }
    }

    private async Task<string> ReadSitemapAsync(Uri uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MaxSitemapBytes)
            throw new InvalidDataException($"Sitemap is larger than {MaxSitemapBytes} bytes.");

        await using var raw = await response.Content.ReadAsStreamAsync(ct);

        // Sitemaps are commonly served gzipped under a .gz name. Content-Encoding
        // is handled by the handler; this is the other convention.
        var gzipped = uri.AbsolutePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
        await using var body = gzipped ? new GZipStream(raw, CompressionMode.Decompress) : raw;

        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;

        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxSitemapBytes)
                throw new InvalidDataException($"Sitemap is larger than {MaxSitemapBytes} bytes.");

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
