using System.Net;
using RecipeApi.Application.Dtos;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests;

public class FavoritesTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private async Task<List<Guid>> SeedRecipeIdsAsync(int count) =>
        (await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>("/api/recipes?pageSize=50"))
        .Items.Take(count).Select(r => r.Id).ToList();

    private Task<PagedResult<RecipeSummaryDto>> FavoritesAsync(string token, string query = "") =>
        Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>($"/api/favorites{query}", token);

    private async Task SaveAsync(string token, Guid recipeId, HttpStatusCode expected = HttpStatusCode.NoContent)
    {
        using var response = await Client.SendAsync(HttpMethod.Post, $"/api/favorites/{recipeId}", token: token);
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/favorites")]
    [InlineData("POST", "/api/favorites/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/favorites/11111111-1111-1111-1111-111111111111")]
    public async Task Every_route_requires_an_account(string method, string url)
    {
        using var response = await Client.SendAsync(new HttpMethod(method), url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Saved_recipes_come_back_newest_first()
    {
        var token = await Client.RegisterAsync("saver");
        var ids = await SeedRecipeIdsAsync(3);

        foreach (var id in ids)
            await SaveAsync(token, id);

        var favorites = await FavoritesAsync(token);

        Assert.Equal(3, favorites.TotalCount);
        Assert.Equal(ids.AsEnumerable().Reverse(), favorites.Items.Select(r => r.Id));
        Assert.All(favorites.Items, r => Assert.True(r.IsFavorited));
    }

    /// <summary>
    /// Both writes are idempotent by design: the caller asked for a state, and
    /// afterwards that state holds. Saving twice is not a conflict, and unsaving
    /// something that was never saved is not a 404.
    /// </summary>
    [Fact]
    public async Task Saving_and_unsaving_are_idempotent()
    {
        var token = await Client.RegisterAsync("repeater");
        var id = (await SeedRecipeIdsAsync(1)).Single();

        await SaveAsync(token, id);
        await SaveAsync(token, id);
        Assert.Equal(1, (await FavoritesAsync(token)).TotalCount);

        using (var first = await Client.SendAsync(HttpMethod.Delete, $"/api/favorites/{id}", token: token))
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using (var second = await Client.SendAsync(HttpMethod.Delete, $"/api/favorites/{id}", token: token))
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        Assert.Equal(0, (await FavoritesAsync(token)).TotalCount);
    }

    [Fact]
    public async Task Saving_an_unknown_recipe_is_404()
    {
        var token = await Client.RegisterAsync("saver");

        using var response = await Client.SendAsync(HttpMethod.Post, $"/api/favorites/{Guid.Empty}", token: token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Favorites_are_private_to_each_account()
    {
        var alice = await Client.RegisterAsync("alice");
        var bob = await Client.RegisterAsync("bob");
        var id = (await SeedRecipeIdsAsync(1)).Single();

        await SaveAsync(alice, id);

        Assert.Equal(1, (await FavoritesAsync(alice)).TotalCount);
        Assert.Equal(0, (await FavoritesAsync(bob)).TotalCount);
    }

    [Fact]
    public async Task IsFavorited_is_per_caller_in_search_results()
    {
        var alice = await Client.RegisterAsync("alice");
        var bob = await Client.RegisterAsync("bob");
        var saved = (await SeedRecipeIdsAsync(2)).ToHashSet();

        foreach (var id in saved)
            await SaveAsync(alice, id);

        var forAlice = await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>("/api/recipes?pageSize=50", alice);
        var forBob = await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>("/api/recipes?pageSize=50", bob);
        var forGuest = await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>("/api/recipes?pageSize=50");

        Assert.Equal(saved, forAlice.Items.Where(r => r.IsFavorited).Select(r => r.Id).ToHashSet());
        Assert.DoesNotContain(forBob.Items, r => r.IsFavorited);
        Assert.DoesNotContain(forGuest.Items, r => r.IsFavorited);
    }

    [Fact]
    public async Task IsFavorited_is_per_caller_on_the_detail_endpoint()
    {
        var alice = await Client.RegisterAsync("alice");
        var bob = await Client.RegisterAsync("bob");
        var id = (await SeedRecipeIdsAsync(1)).Single();

        await SaveAsync(alice, id);

        Assert.True((await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}", alice)).IsFavorited);
        Assert.False((await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}", bob)).IsFavorited);
        Assert.False((await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}")).IsFavorited);
    }

    [Fact]
    public async Task Favorites_page_and_clamp_like_search()
    {
        var token = await Client.RegisterAsync("pager");
        var ids = await SeedRecipeIdsAsync(3);

        foreach (var id in ids)
            await SaveAsync(token, id);

        var seen = new List<Guid>();
        for (var page = 1; ; page++)
        {
            var result = await FavoritesAsync(token, $"?page={page}&pageSize=1");
            if (result.Items.Count == 0)
                break;

            seen.AddRange(result.Items.Select(r => r.Id));
            if (page >= result.TotalPages)
                break;
        }

        Assert.Equal(3, seen.Count);
        Assert.Equal(3, seen.Distinct().Count());

        var clamped = await FavoritesAsync(token, "?page=0&pageSize=9999");
        Assert.Equal(1, clamped.Page);
        Assert.Equal(100, clamped.PageSize);
    }

    /// <summary>
    /// The favorite is a row referencing a recipe. Deleting the recipe has to take
    /// it with it, or the next favorites listing joins to a row that is gone.
    /// </summary>
    [Fact]
    public async Task Deleting_a_recipe_removes_it_from_everyones_favorites()
    {
        var owner = await Client.RegisterAsync("owner");
        var saver = await Client.RegisterAsync("saver");

        using var created = await Client.SendAsync(HttpMethod.Post, "/api/recipes", Api.NewRecipe(), owner);
        var id = await created.ReadAsync<Guid>();

        await SaveAsync(saver, id);
        Assert.Equal(1, (await FavoritesAsync(saver)).TotalCount);

        using var deleted = await Client.SendAsync(HttpMethod.Delete, $"/api/recipes/{id}", token: owner);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal(0, (await FavoritesAsync(saver)).TotalCount);
    }

    [Fact]
    public async Task Any_recipe_can_be_saved_regardless_of_who_owns_it()
    {
        var token = await Client.RegisterAsync("saver");
        var scraped = (await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>(
            "/api/recipes?search=Pulled%20Pork")).Items.Single().Id;

        await SaveAsync(token, scraped);

        var detail = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{scraped}", token);
        Assert.True(detail.IsFavorited);
        Assert.False(detail.IsEditable);   // saving is not ownership
    }
}
