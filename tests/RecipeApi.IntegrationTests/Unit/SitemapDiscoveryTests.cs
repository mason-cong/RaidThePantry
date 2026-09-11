using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApi.Infrastructure.Scraping;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests.Unit;

/// <summary>
/// Discovery reads the site's own sitemaps to decide what is worth fetching.
///
/// The limit is the part that matters most. Real recipe sitemaps run to tens of
/// thousands of URLs — allrecipes.com lists over 13,000 in one of four — so a
/// discovery that quietly ignored its cap would hand `scrape` days of requests
/// against somebody else's origin.
/// </summary>
public class SitemapDiscoveryTests : IAsyncLifetime
{
    private FixtureSite _site = null!;
    private HttpClient _client = null!;
    private SitemapDiscovery _discovery = null!;

    public async Task InitializeAsync()
    {
        _site = await FixtureSite.StartAsync();

        var options = Options.Create(new ScrapingOptions
        {
            AllowLoopbackHosts = true,
            PolitenessDelaySeconds = 0,
            UserAgentToken = "RecipeFinderBot"
        });

        _client = new HttpClient(ScrapingHttpDefaults.Handler(options.Value, followRedirects: true));
        var robotsClient = new HttpClient(ScrapingHttpDefaults.Handler(options.Value, followRedirects: true));

        _discovery = new SitemapDiscovery(
            _client,
            new RobotsTxtChecker(robotsClient, options, NullLogger<RobotsTxtChecker>.Instance),
            options,
            NullLogger<SitemapDiscovery>.Instance);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _site.DisposeAsync();
    }

    private Uri Site => new(_site.BaseUrl);

    [Fact]
    public async Task Follows_a_sitemap_index_into_its_children()
    {
        var result = await _discovery.DiscoverAsync(Site, new DiscoveryRequest(Limit: 100), default);

        // The index itself plus the one child it points at.
        Assert.Equal(2, result.SitemapsFetched);
        Assert.Contains(result.Urls, u => u.EndsWith("/recipe/one"));
    }

    [Fact]
    public async Task Match_filters_to_the_pages_that_are_actually_recipes()
    {
        var result = await _discovery.DiscoverAsync(
            Site, new DiscoveryRequest(Limit: 100, Match: "/recipe/"), default);

        Assert.All(result.Urls, url => Assert.Contains("/recipe/", url));
        Assert.DoesNotContain(result.Urls, u => u.Contains("/article/"));
    }

    /// <summary>
    /// The cap is the whole safety story for this command, so it is checked
    /// against a sitemap that holds more than the limit asks for.
    /// </summary>
    [Fact]
    public async Task Stops_at_the_limit()
    {
        var result = await _discovery.DiscoverAsync(
            Site, new DiscoveryRequest(Limit: 2, Match: "/recipe/"), default);

        Assert.Equal(2, result.Urls.Count);
    }

    /// <summary>
    /// robots.txt disallows /blocked/ for this crawler, and the sitemap lists a
    /// page under it anyway — sites do publish URLs they also disallow. The list
    /// handed to `scrape` must not contain it.
    /// </summary>
    [Fact]
    public async Task A_disallowed_url_never_reaches_the_list()
    {
        // "recipe" rather than "/recipe/": the blocked page is
        // /blocked/secret-recipe, and the narrower filter would drop it before
        // robots.txt was ever consulted — making the rejection counter read zero
        // for the wrong reason.
        var result = await _discovery.DiscoverAsync(
            Site, new DiscoveryRequest(Limit: 100, Match: "recipe"), default);

        Assert.DoesNotContain(result.Urls, u => u.Contains("/blocked/"));
        Assert.True(result.RejectedByRobots >= 1,
            $"expected the blocked page to be counted, got {result.RejectedByRobots}");
    }

    /// <summary>
    /// A sitemap may list anything, including other people's hosts. Following
    /// those would widen the crawl past the host whose robots.txt was read.
    /// </summary>
    [Fact]
    public async Task Off_site_urls_and_off_site_sitemaps_are_ignored()
    {
        var result = await _discovery.DiscoverAsync(Site, new DiscoveryRequest(Limit: 100), default);

        Assert.DoesNotContain(result.Urls, u => u.Contains("elsewhere.invalid"));

        // robots.txt also declares an off-site sitemap; it must not have been fetched.
        Assert.Equal(2, result.SitemapsFetched);
    }

    [Fact]
    public async Task A_host_the_guard_rejects_is_refused_outright()
    {
        var options = Options.Create(new ScrapingOptions { AllowLoopbackHosts = false });

        using var client = new HttpClient(ScrapingHttpDefaults.Handler(options.Value));
        using var robotsClient = new HttpClient(ScrapingHttpDefaults.Handler(options.Value));

        var guarded = new SitemapDiscovery(
            client,
            new RobotsTxtChecker(robotsClient, options, NullLogger<RobotsTxtChecker>.Instance),
            options,
            NullLogger<SitemapDiscovery>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => guarded.DiscoverAsync(new Uri("http://169.254.169.254/"), new DiscoveryRequest(), default));
    }
}
