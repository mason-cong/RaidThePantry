using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// Honors robots.txt for the bulk crawl.
///
/// A one-second delay is politeness; robots.txt is the site actually telling you
/// what it will tolerate, and ignoring it is how a crawler gets blocked or worse.
/// Rules are fetched once per host and cached for the run.
///
/// This covers what the crawl needs — user-agent groups, Allow/Disallow with
/// longest-match precedence, wildcards, and Crawl-delay. It is not a complete
/// RFC 9309 implementation: sitemap directives are ignored, and a robots.txt
/// that cannot be fetched is treated as permissive, matching common practice.
/// </summary>
public partial class RobotsTxtChecker(
    HttpClient http,
    IOptions<ScrapingOptions> options,
    ILogger<RobotsTxtChecker> logger)
{
    private readonly ScrapingOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Task<RobotsRules>> _cache = new();

    public Task<RobotsRules> GetRulesAsync(Uri uri, CancellationToken ct) =>
        _cache.GetOrAdd(uri.GetLeftPart(UriPartial.Authority), authority => LoadAsync(authority, ct));

    public async Task<bool> IsAllowedAsync(Uri uri, CancellationToken ct)
    {
        var rules = await GetRulesAsync(uri, ct);
        return rules.Allows(uri.PathAndQuery);
    }

    private async Task<RobotsRules> LoadAsync(string authority, CancellationToken ct)
    {
        try
        {
            var robotsUri = new Uri(new Uri(authority), "/robots.txt");

            if (!await FetchableUrl.IsHostAllowedAsync(robotsUri, _options.AllowLoopbackHosts, ct))
                return RobotsRules.DenyAll;

            using var response = await http.GetAsync(robotsUri, ct);

            // 404 means no rules, which means no restrictions. A 5xx arguably
            // means "back off", but treating it as permissive matches what most
            // crawlers do and keeps one flaky host from stalling a whole run.
            if (!response.IsSuccessStatusCode)
                return RobotsRules.AllowAll;

            var text = await response.Content.ReadAsStringAsync(ct);
            return Parse(text, _options.UserAgentToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogInformation("Could not read robots.txt for {Authority}; proceeding", authority);
            return RobotsRules.AllowAll;
        }
    }

    internal static RobotsRules Parse(string text, string userAgentToken)
    {
        // Collect the group for our token and the wildcard group separately; a
        // group naming this crawler wins outright over "*".
        var specific = new RobotsRules();
        var wildcard = new RobotsRules();

        // Sitemap is a file-level directive, not part of any user-agent group,
        // so it is gathered separately and attached to whichever group wins.
        var sitemaps = new List<string>();

        var active = new List<RobotsRules>();
        var lastLineWasUserAgent = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;

            var hash = line.IndexOf('#');
            if (hash >= 0)
                line = line[..hash];

            line = line.Trim();
            if (line.Length == 0)
                continue;

            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var field = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            switch (field)
            {
                case "user-agent":
                    // Consecutive User-agent lines share one group of rules.
                    if (!lastLineWasUserAgent)
                        active.Clear();

                    if (value == "*")
                        active.Add(wildcard);
                    else if (value.Contains(userAgentToken, StringComparison.OrdinalIgnoreCase))
                        active.Add(specific);

                    lastLineWasUserAgent = true;
                    continue;

                case "disallow":
                    foreach (var rules in active)
                        rules.Add(value, allow: false);
                    break;

                case "allow":
                    foreach (var rules in active)
                        rules.Add(value, allow: true);
                    break;

                case "crawl-delay":
                    if (double.TryParse(value, out var seconds))
                        foreach (var rules in active)
                            rules.CrawlDelay = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 60));
                    break;

                case "sitemap":
                    if (Uri.TryCreate(value, UriKind.Absolute, out var sitemap) &&
                        (sitemap.Scheme == Uri.UriSchemeHttp || sitemap.Scheme == Uri.UriSchemeHttps))
                    {
                        sitemaps.Add(sitemap.ToString());
                    }
                    break;
            }

            lastLineWasUserAgent = false;
        }

        var winner = specific.HasRules ? specific : wildcard;
        winner.Sitemaps = sitemaps;
        return winner;
    }

    [GeneratedRegex(@"\*+")]
    internal static partial Regex Wildcards();
}

public class RobotsRules
{
    private readonly List<(Regex Pattern, int Length, bool Allow)> _rules = [];

    public TimeSpan? CrawlDelay { get; set; }

    /// <summary>
    /// Sitemap URLs the site declares. This is how <see cref="SitemapDiscovery"/>
    /// finds recipe pages: the publisher listing what it wants indexed beats
    /// guessing at URLs or spidering links out of a homepage.
    /// </summary>
    public IReadOnlyList<string> Sitemaps { get; set; } = [];

    public bool HasRules => _rules.Count > 0 || CrawlDelay is not null;

    public static RobotsRules AllowAll => new();

    public static RobotsRules DenyAll
    {
        get
        {
            var rules = new RobotsRules();
            rules.Add("/", allow: false);
            return rules;
        }
    }

    public void Add(string path, bool allow)
    {
        // "Disallow:" with an empty value means allow everything — it is how a
        // site opts back in after a broader rule.
        if (string.IsNullOrEmpty(path))
        {
            if (!allow) _rules.Clear();
            return;
        }

        _rules.Add((ToRegex(path), path.Length, allow));
    }

    /// <summary>
    /// Longest matching rule wins, and Allow beats Disallow at equal length —
    /// the precedence RFC 9309 specifies, and the reason a site can carve an
    /// exception out of a broad Disallow.
    /// </summary>
    public bool Allows(string pathAndQuery)
    {
        (int Length, bool Allow)? best = null;

        foreach (var (pattern, length, allow) in _rules)
        {
            if (!pattern.IsMatch(pathAndQuery))
                continue;

            if (best is null || length > best.Value.Length || (length == best.Value.Length && allow))
                best = (length, allow);
        }

        return best?.Allow ?? true;
    }

    private static Regex ToRegex(string path)
    {
        var anchoredAtEnd = path.EndsWith('$');
        if (anchoredAtEnd)
            path = path[..^1];

        var pattern = string.Join(".*",
            RobotsTxtChecker.Wildcards().Split(path).Select(Regex.Escape));

        return new Regex("^" + pattern + (anchoredAtEnd ? "$" : string.Empty), RegexOptions.None);
    }
}
