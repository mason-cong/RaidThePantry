using Microsoft.EntityFrameworkCore;
using RecipeApi.Domain.Staging;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests;

/// <summary>
/// Stage one of the bulk crawl: fetch into staging.ScrapedPages, touch nothing
/// else. Run against the real fixture server, because what is being tested is
/// the behaviour around a real fetch — robots.txt, failures mid-batch, and what
/// gets stored for each outcome.
/// </summary>
public class ScrapeJobTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private FixtureSite? _site;

    private async Task<FixtureSite> SiteAsync() => _site ??= await FixtureSite.StartAsync();

    public override async Task DisposeAsync()
    {
        if (_site is not null)
            await _site.DisposeAsync();
    }

    /// <summary>
    /// Staging is shared across the tests in this class — the fixture re-seeds
    /// once per class, not per test — so each test starts from an empty table.
    /// </summary>
    private static async Task ClearStagingAsync(WorkerHarness harness) =>
        await harness.Db.ScrapedPages.ExecuteDeleteAsync();

    private async Task<ScrapedPage?> PageAsync(WorkerHarness harness, string url) =>
        await harness.Db.ScrapedPages.AsNoTracking().FirstOrDefaultAsync(p => p.Url == url);

    [Fact]
    public async Task A_page_with_a_recipe_is_staged_as_json_ld_only()
    {
        var site = await SiteAsync();
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);

        var summary = await harness.Scraper().RunAsync([site.Url("/simple")], refetch: false, default);

        Assert.Equal(1, summary.Fetched);
        Assert.Equal(1, summary.Extracted);

        var page = await PageAsync(harness, site.Url("/simple"));
        Assert.NotNull(page);
        Assert.Equal(ExtractionStatus.Extracted, page!.Extraction);
        Assert.NotNull(page.ExtractedJsonLd);

        // Only the recipe node is kept — a few KB instead of the whole document.
        // Storing both would multiply the corpus for nothing.
        Assert.Null(page.RawHtml);
    }

    /// <summary>
    /// The inverse, and the reason it matters: pages where extraction failed are
    /// exactly the corpus HtmlFallbackParser has to be written against, so the
    /// HTML is the one thing worth keeping for them.
    /// </summary>
    [Fact]
    public async Task A_page_with_no_recipe_keeps_its_html_instead()
    {
        var site = await SiteAsync();
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);

        var summary = await harness.Scraper().RunAsync([site.Url("/no-jsonld")], refetch: false, default);

        Assert.Equal(1, summary.NoRecipe);

        var page = await PageAsync(harness, site.Url("/no-jsonld"));
        Assert.Equal(ExtractionStatus.NoRecipeFound, page!.Extraction);
        Assert.NotNull(page.RawHtml);
        Assert.Null(page.ExtractedJsonLd);
    }

    [Fact]
    public async Task One_failing_page_does_not_end_the_batch()
    {
        var site = await SiteAsync();
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);

        // The failure comes first on purpose: if it aborted the run, the page
        // after it would never be fetched.
        var summary = await harness.Scraper().RunAsync(
            [site.Url("/boom"), site.Url("/simple")], refetch: false, default);

        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Extracted);

        var failed = await PageAsync(harness, site.Url("/boom"));
        Assert.Equal(ExtractionStatus.FetchFailed, failed!.Extraction);
        Assert.Equal(500, failed.HttpStatus);
        Assert.NotNull(failed.ErrorMessage);

        Assert.Equal(ExtractionStatus.Extracted,
            (await PageAsync(harness, site.Url("/simple")))!.Extraction);
    }

    /// <summary>
    /// The fixture's robots.txt disallows /blocked/ for this crawler, while the
    /// wildcard group allows it — so this also proves the named group is the one
    /// being applied.
    /// </summary>
    [Fact]
    public async Task Robots_txt_keeps_the_crawler_out()
    {
        var site = await SiteAsync();
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);

        var url = site.Url("/blocked/secret-recipe");
        var summary = await harness.Scraper().RunAsync([url], refetch: false, default);

        Assert.Equal(1, summary.BlockedByRobots);
        Assert.Equal(0, summary.Fetched);

        // Not merely unfetched — never staged at all.
        Assert.Null(await PageAsync(harness, url));
    }

    [Fact]
    public async Task Re_running_skips_pages_that_are_already_staged()
    {
        var site = await SiteAsync();
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);

        await harness.Scraper().RunAsync([site.Url("/simple")], refetch: false, default);
        var second = await harness.Scraper().RunAsync([site.Url("/simple")], refetch: false, default);

        Assert.Equal(0, second.Fetched);
        Assert.Equal(1, second.Skipped);
    }

    /// <summary>
    /// A refetch replaces the stored content, so whatever transform ran over the
    /// old bytes no longer describes what is there. Promotion state has to go
    /// back to unpromoted or the new content is never transformed.
    /// </summary>
    [Fact]
    public async Task Refetch_fetches_again_and_resets_promotion_state()
    {
        var site = await SiteAsync();
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);

        await harness.Scraper().RunAsync([site.Url("/simple")], refetch: false, default);
        await harness.Promoter().RunAsync(repromote: false, default);

        var promoted = await PageAsync(harness, site.Url("/simple"));
        Assert.Equal(PromotionStatus.Promoted, promoted!.Promotion);

        var again = await harness.Scraper().RunAsync([site.Url("/simple")], refetch: true, default);

        Assert.Equal(1, again.Fetched);
        Assert.Equal(0, again.Skipped);

        var refetched = await PageAsync(harness, site.Url("/simple"));
        Assert.Equal(PromotionStatus.NotPromoted, refetched!.Promotion);
        Assert.Equal(0, refetched.ParserVersion);
    }

    [Fact]
    public async Task A_malformed_url_is_counted_rather_than_thrown()
    {
        var site = await SiteAsync();
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);

        var summary = await harness.Scraper().RunAsync(
            ["not-a-url", "ftp://example.com/recipe", site.Url("/simple")], refetch: false, default);

        Assert.Equal(2, summary.Invalid);
        Assert.Equal(1, summary.Fetched);
    }

    /// <summary>
    /// The crawl reaches whatever the URL list names, so the SSRF guard has to
    /// apply here exactly as it does to the API's import endpoint. This is the
    /// case that went unprotected while the Worker used a bare handler.
    /// </summary>
    [Fact]
    public async Task A_private_address_in_the_crawl_list_is_refused()
    {
        using var harness = new WorkerHarness(Fixture.Factory, o => o.AllowLoopbackHosts = false);
        await ClearStagingAsync(harness);

        var summary = await harness.Scraper().RunAsync(
            ["http://169.254.169.254/latest/meta-data/"], refetch: false, default);

        Assert.Equal(0, summary.Extracted);

        // Either refused before the request or refused at connect; both are the
        // guard working, and neither may produce a usable recipe.
        var page = await PageAsync(harness, "http://169.254.169.254/latest/meta-data/");
        Assert.True(page is null || page.Extraction == ExtractionStatus.FetchFailed);
    }
}
