using System.Net;
using Microsoft.EntityFrameworkCore;
using RecipeApi.Application.Dtos;
using RecipeApi.Domain;
using RecipeApi.Domain.Staging;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests;

/// <summary>
/// Stage two: turn staged pages into recipes, with no network access at all.
///
/// The re-promotion cases are the ones that matter most. Re-transforming the
/// corpus is meant to be a routine thing to do after a normalizer change, and
/// the difference between updating rows and replacing them is invisible until
/// somebody notices their saved recipes are gone.
/// </summary>
public class PromoteJobTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private FixtureSite? _site;

    private async Task<FixtureSite> SiteAsync() => _site ??= await FixtureSite.StartAsync();

    public override async Task DisposeAsync()
    {
        if (_site is not null)
            await _site.DisposeAsync();
    }

    private static async Task ClearStagingAsync(WorkerHarness harness) =>
        await harness.Db.ScrapedPages.ExecuteDeleteAsync();

    /// <summary>Scrapes one fixture page and returns its URL.</summary>
    private async Task<string> StageAsync(WorkerHarness harness, string path)
    {
        var site = await SiteAsync();
        var url = site.Url(path);
        await harness.Scraper().RunAsync([url], refetch: false, default);
        return url;
    }

    private async Task<Recipe?> PromotedRecipeAsync(WorkerHarness harness, string url)
    {
        var page = await harness.Db.ScrapedPages.AsNoTracking().FirstAsync(p => p.Url == url);
        return page.PromotedRecipeId is null
            ? null
            : await harness.Db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == page.PromotedRecipeId);
    }

    [Fact]
    public async Task Promoted_recipes_have_no_owner_which_is_what_makes_them_read_only()
    {
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);
        var url = await StageAsync(harness, "/simple");

        var summary = await harness.Promoter().RunAsync(repromote: false, default);
        Assert.Equal(1, summary.Created);

        var recipe = await PromotedRecipeAsync(harness, url);
        Assert.NotNull(recipe);
        Assert.Equal(RecipeSourceType.BulkScrape, recipe!.SourceType);
        Assert.Equal(url, recipe.SourceUrl);

        // The ownership rule is "creator only", so a null owner means nobody can
        // edit it — which is the intended state for scraped content.
        Assert.Null(recipe.CreatedByUserId);
    }

    [Fact]
    public async Task Promoting_twice_changes_nothing_the_second_time()
    {
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);
        await StageAsync(harness, "/simple");

        await harness.Promoter().RunAsync(repromote: false, default);
        var second = await harness.Promoter().RunAsync(repromote: false, default);

        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Updated);
        Assert.Equal(0, second.Rejected);
    }

    /// <summary>
    /// Without --repromote, an already-promoted page is left alone even when the
    /// parser version has moved on. Re-transforming the whole corpus is a
    /// deliberate act, not something a routine promote does by surprise.
    /// </summary>
    [Fact]
    public async Task A_bumped_parser_version_alone_does_not_re_promote()
    {
        string url;
        using (var first = new WorkerHarness(Fixture.Factory))
        {
            await ClearStagingAsync(first);
            url = await StageAsync(first, "/simple");
            await first.Promoter().RunAsync(repromote: false, default);
        }

        using var later = new WorkerHarness(Fixture.Factory, o => o.ParserVersion = 2);
        var summary = await later.Promoter().RunAsync(repromote: false, default);

        Assert.Equal(0, summary.Updated);
        Assert.Equal(1, (await later.Db.ScrapedPages.AsNoTracking().FirstAsync(p => p.Url == url)).ParserVersion);
    }

    [Fact]
    public async Task Repromote_updates_in_place_and_keeps_the_recipe_id()
    {
        string url;
        Guid originalId;

        using (var first = new WorkerHarness(Fixture.Factory))
        {
            await ClearStagingAsync(first);
            url = await StageAsync(first, "/simple");
            await first.Promoter().RunAsync(repromote: false, default);
            originalId = (await PromotedRecipeAsync(first, url))!.Id;
        }

        using var later = new WorkerHarness(Fixture.Factory, o => o.ParserVersion = 2);
        var summary = await later.Promoter().RunAsync(repromote: true, default);

        Assert.Equal(1, summary.Updated);
        Assert.Equal(0, summary.Created);

        var after = await PromotedRecipeAsync(later, url);

        // Delete-and-recreate would also report success and leave a correct
        // looking recipe behind. The id is the thing that says which happened.
        Assert.Equal(originalId, after!.Id);
        Assert.Equal(2, (await later.Db.ScrapedPages.AsNoTracking().FirstAsync(p => p.Url == url)).ParserVersion);
    }

    /// <summary>
    /// The consequence of the test above, spelled out end to end: a saved recipe
    /// has to still be saved after the corpus is re-transformed. Favorites cascade
    /// on recipe delete, so a delete-and-recreate promote would silently empty
    /// everyone's saved list.
    /// </summary>
    [Fact]
    public async Task A_saved_recipe_survives_a_repromote()
    {
        string url;
        Guid recipeId;

        using (var first = new WorkerHarness(Fixture.Factory))
        {
            await ClearStagingAsync(first);
            url = await StageAsync(first, "/simple");
            await first.Promoter().RunAsync(repromote: false, default);
            recipeId = (await PromotedRecipeAsync(first, url))!.Id;
        }

        var token = await Client.RegisterAsync("favouriter");
        using (var save = await Client.SendAsync(HttpMethod.Post, $"/api/favorites/{recipeId}", token: token))
            Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);

        using (var later = new WorkerHarness(Fixture.Factory, o => o.ParserVersion = 2))
            await later.Promoter().RunAsync(repromote: true, default);

        var saved = await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>("/api/favorites", token);

        Assert.Equal(1, saved.TotalCount);
        Assert.Equal(recipeId, saved.Items[0].Id);
    }

    /// <summary>
    /// If a recipe gained an owner after being promoted, it is somebody's now and
    /// the crawler must not write over it. Rejecting is the only safe answer —
    /// the alternative is a bulk job silently overwriting a person's edits.
    /// </summary>
    [Fact]
    public async Task A_recipe_that_gained_an_owner_is_never_overwritten()
    {
        string url;
        Guid recipeId;

        using (var first = new WorkerHarness(Fixture.Factory))
        {
            await ClearStagingAsync(first);
            url = await StageAsync(first, "/simple");
            await first.Promoter().RunAsync(repromote: false, default);
            recipeId = (await PromotedRecipeAsync(first, url))!.Id;
        }

        var token = await Client.RegisterAsync("claimant");
        var me = await Client.GetJsonAsync<CurrentUserDto>("/api/auth/me", token);

        // Claim it directly: there is no API for adopting a scraped recipe, which
        // is the point — this is simulating a state the database could reach,
        // not an action a user can take today.
        using (var claiming = new WorkerHarness(Fixture.Factory))
        {
            await claiming.Db.Recipes
                .Where(r => r.Id == recipeId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedByUserId, me.Id));
        }

        using var later = new WorkerHarness(Fixture.Factory, o => o.ParserVersion = 2);
        var summary = await later.Promoter().RunAsync(repromote: true, default);

        Assert.Equal(1, summary.Rejected);
        Assert.Equal(0, summary.Updated);

        var page = await later.Db.ScrapedPages.AsNoTracking().FirstAsync(p => p.Url == url);
        Assert.Equal(PromotionStatus.Rejected, page.Promotion);
        Assert.Contains("owner", page.ErrorMessage!, StringComparison.OrdinalIgnoreCase);

        // And the owner still has it.
        var stillOwned = await later.Db.Recipes.AsNoTracking().FirstAsync(r => r.Id == recipeId);
        Assert.Equal(me.Id, stillOwned.CreatedByUserId);
    }

    /// <summary>
    /// A user importing a URL through the API and the crawler reaching the same
    /// URL later is an ordinary collision — SourceUrl is unique. Promote adopts
    /// the existing recipe rather than fighting the index over it.
    /// </summary>
    [Fact]
    public async Task A_url_already_imported_through_the_api_is_adopted_not_duplicated()
    {
        var site = await SiteAsync();
        var url = site.Url("/simple");

        var token = await Client.RegisterAsync("importer");
        using var imported = await Client.SendAsync(
            HttpMethod.Post, "/api/import/url", new { url }, token);
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        var importedId = await imported.ReadAsync<Guid>();

        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);
        await harness.Scraper().RunAsync([url], refetch: false, default);

        var summary = await harness.Promoter().RunAsync(repromote: false, default);

        Assert.Equal(1, summary.Linked);
        Assert.Equal(0, summary.Created);

        var page = await harness.Db.ScrapedPages.AsNoTracking().FirstAsync(p => p.Url == url);
        Assert.Equal(importedId, page.PromotedRecipeId);

        // One recipe for that URL, not two.
        Assert.Equal(1, await harness.Db.Recipes.CountAsync(r => r.SourceUrl == url));
    }

    [Fact]
    public async Task Pages_with_nothing_extracted_are_left_alone()
    {
        using var harness = new WorkerHarness(Fixture.Factory);
        await ClearStagingAsync(harness);

        await StageAsync(harness, "/no-jsonld");   // NoRecipeFound
        await StageAsync(harness, "/boom");        // FetchFailed

        var summary = await harness.Promoter().RunAsync(repromote: false, default);

        Assert.Equal(0, summary.Created);
        Assert.Equal(0, summary.Rejected);

        Assert.All(await harness.Db.ScrapedPages.AsNoTracking().ToListAsync(),
            page => Assert.Equal(PromotionStatus.NotPromoted, page.Promotion));
    }
}
