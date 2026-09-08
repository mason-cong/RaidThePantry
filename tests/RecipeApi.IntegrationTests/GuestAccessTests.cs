using System.Net;
using System.Text.Json;
using RecipeApi.Application.Dtos;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests;

/// <summary>
/// Guest-first is the product's core constraint: browsing must never require an
/// account. Auth middleware is easy to wire too broadly, and this is what would
/// catch that.
/// </summary>
public class GuestAccessTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Health_reports_the_database_is_reachable()
    {
        var body = await Client.GetJsonAsync<JsonElement>("/health");

        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("connected", body.GetProperty("database").GetString());
    }

    [Theory]
    [InlineData("/api/recipes")]
    [InlineData("/api/recipes?search=chicken")]
    [InlineData("/api/cuisines")]
    [InlineData("/api/ingredients")]
    [InlineData("/api/ingredients?search=chick")]
    public async Task Read_endpoints_serve_anonymous_callers(string url)
    {
        using var response = await Client.GetAsync(url, token: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Seed_data_is_present_and_shaped_as_expected()
    {
        var page = await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>("/api/recipes?pageSize=50");

        Assert.Equal(18, page.TotalCount);
        Assert.Contains(page.Items, r => r.Title == "Margherita Pizza");
        Assert.All(page.Items, r => Assert.False(r.IsFavorited));
    }

    [Fact]
    public async Task Recipe_detail_is_anonymous_and_reports_no_permissions()
    {
        var page = await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>("/api/recipes?search=Margherita");
        var id = page.Items.Single().Id;

        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");

        Assert.Equal("Margherita Pizza", recipe.Title);
        Assert.False(recipe.IsEditable);
        Assert.False(recipe.IsFavorited);
        Assert.NotEmpty(recipe.Steps);
        Assert.NotEmpty(recipe.Ingredients);
    }

    [Fact]
    public async Task Unknown_recipe_id_is_404()
    {
        using var response = await Client.GetAsync($"/api/recipes/{Guid.Empty}", token: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cuisines_and_ingredients_carry_usage_counts()
    {
        var cuisines = await Client.GetJsonAsync<List<CuisineDto>>("/api/cuisines");
        var ingredients = await Client.GetJsonAsync<List<IngredientDto>>("/api/ingredients?search=chicken%20breast");

        Assert.Equal(14, cuisines.Count);
        Assert.All(cuisines, c => Assert.True(c.RecipeCount > 0));

        var chicken = Assert.Single(ingredients, i => i.Name == "chicken breast");
        Assert.Equal(3, chicken.RecipeCount);
    }
}
