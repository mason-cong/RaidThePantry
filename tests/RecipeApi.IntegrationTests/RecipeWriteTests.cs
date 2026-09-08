using System.Net;
using RecipeApi.Application.Dtos;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests;

public class RecipeWriteTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private async Task<Guid> CreateAsync(string token, CreateRecipeRequest? request = null)
    {
        using var response = await Client.SendAsync(
            HttpMethod.Post, "/api/recipes", request ?? Api.NewRecipe(), token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<Guid>();
    }

    private async Task<Guid> ScrapedRecipeIdAsync()
    {
        var page = await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>(
            "/api/recipes?search=Pulled%20Pork");

        return page.Items.Single().Id;
    }

    // ------------------------------------------------------------ gating

    [Theory]
    [InlineData("POST", "/api/recipes")]
    [InlineData("PUT", "/api/recipes/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/recipes/11111111-1111-1111-1111-111111111111")]
    public async Task Writes_require_an_account(string method, string url)
    {
        using var response = await Client.SendAsync(new HttpMethod(method), url, Api.NewRecipe());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Creating_sets_the_caller_as_owner()
    {
        var token = await Client.RegisterAsync("owner");
        var id = await CreateAsync(token);

        var asOwner = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}", token);
        var asGuest = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");

        Assert.True(asOwner.IsEditable);
        Assert.False(asGuest.IsEditable);
        Assert.Equal(RecipeApi.Domain.RecipeSourceType.Manual, asOwner.SourceType);
        Assert.Null(asOwner.SourceUrl);
    }

    // --------------------------------------------------------- ownership
    // A null CreatedByUserId means scraped content, which belongs to nobody and
    // so is editable by nobody — not by the first caller to claim it.

    [Fact]
    public async Task Another_user_cannot_modify_your_recipe()
    {
        var owner = await Client.RegisterAsync("owner");
        var stranger = await Client.RegisterAsync("stranger");
        var id = await CreateAsync(owner);

        using var put = await Client.SendAsync(HttpMethod.Put, $"/api/recipes/{id}", Api.NewRecipe(), stranger);
        using var delete = await Client.SendAsync(HttpMethod.Delete, $"/api/recipes/{id}", token: stranger);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        // And the recipe is untouched.
        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");
        Assert.NotEqual("Hijacked", recipe.Title);
    }

    [Fact]
    public async Task Scraped_recipes_are_editable_by_nobody()
    {
        var token = await Client.RegisterAsync("claimant");
        var scrapedId = await ScrapedRecipeIdAsync();

        using var put = await Client.SendAsync(HttpMethod.Put, $"/api/recipes/{scrapedId}", Api.NewRecipe(), token);
        using var delete = await Client.SendAsync(HttpMethod.Delete, $"/api/recipes/{scrapedId}", token: token);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    /// <summary>
    /// 403 rather than 404 for someone else's recipe: it demonstrably exists, and
    /// collapsing the two makes a real 404 indistinguishable from a permission
    /// problem when debugging.
    /// </summary>
    [Fact]
    public async Task A_missing_recipe_is_404_and_a_forbidden_one_is_403()
    {
        var owner = await Client.RegisterAsync("owner");
        var stranger = await Client.RegisterAsync("stranger");
        var id = await CreateAsync(owner);

        using var missing = await Client.SendAsync(
            HttpMethod.Put, $"/api/recipes/{Guid.Empty}", Api.NewRecipe(), stranger);
        using var forbidden = await Client.SendAsync(
            HttpMethod.Put, $"/api/recipes/{id}", Api.NewRecipe(), stranger);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task The_owner_can_update_and_delete()
    {
        var token = await Client.RegisterAsync("owner");
        var id = await CreateAsync(token);

        var edit = Api.NewRecipe(title: "Edited Title", steps: ["One.", "Two."]);
        using var put = await Client.SendAsync(HttpMethod.Put, $"/api/recipes/{id}", edit, token);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var updated = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");
        Assert.Equal("Edited Title", updated.Title);
        Assert.Equal(2, updated.Steps.Count);

        using var delete = await Client.SendAsync(HttpMethod.Delete, $"/api/recipes/{id}", token: token);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var gone = await Client.GetAsync($"/api/recipes/{id}", token: null);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    /// <summary>Children are replaced wholesale, so shrinking a recipe shrinks it.</summary>
    [Fact]
    public async Task Updating_replaces_children_rather_than_appending()
    {
        var token = await Client.RegisterAsync("owner");
        var id = await CreateAsync(token, Api.NewRecipe(
            ingredients:
            [
                new CreateIngredientRequest("2 onions", 2, null, null),
                new CreateIngredientRequest("3 carrots", 3, null, null),
                new CreateIngredientRequest("1 tsp salt", 1, "tsp", null)
            ],
            steps: ["One.", "Two.", "Three."]));

        var before = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");
        Assert.Equal(3, before.Ingredients.Count);

        using var put = await Client.SendAsync(HttpMethod.Put, $"/api/recipes/{id}", Api.NewRecipe(
            ingredients: [new CreateIngredientRequest("2 onions", 2, null, null)],
            steps: ["Only one now."]), token);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var after = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");
        Assert.Single(after.Ingredients);
        Assert.Single(after.Steps);
    }

    /// <summary>Ingredients are shared rows; deleting a recipe must not take them.</summary>
    [Fact]
    public async Task Deleting_a_recipe_leaves_shared_ingredients_alone()
    {
        var token = await Client.RegisterAsync("owner");
        var id = await CreateAsync(token, Api.NewRecipe(
            ingredients: [new CreateIngredientRequest("2 boneless chicken breasts", 2, null, null)]));

        using var delete = await Client.SendAsync(HttpMethod.Delete, $"/api/recipes/{id}", token: token);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var ingredients = await Client.GetJsonAsync<List<IngredientDto>>("/api/ingredients?search=chicken%20breast");
        Assert.Contains(ingredients, i => i.Name == "chicken breast");
    }

    // -------------------------------------------------------- normalizing
    // Raw ingredient lines are normalized to a canonical name and resolved to
    // existing rows. Without that, "2 boneless, skinless chicken breasts, diced"
    // becomes its own ingredient and search stops working.

    [Fact]
    public async Task Ingredient_lines_are_normalized_and_deduplicated()
    {
        var token = await Client.RegisterAsync("cook");
        var before = await Client.GetJsonAsync<List<IngredientDto>>("/api/ingredients?limit=300");

        var id = await CreateAsync(token, Api.NewRecipe(ingredients:
        [
            new CreateIngredientRequest("2 boneless, skinless chicken breasts, diced", null, null, null),
            new CreateIngredientRequest("4 cloves garlic, finely minced", null, null, null),
            new CreateIngredientRequest("1/2 cup extra virgin olive oil", null, null, null),
            new CreateIngredientRequest("a handful of fresh basil leaves", null, null, null)
        ]));

        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");
        var names = recipe.Ingredients.Select(i => i.Name).ToList();

        Assert.Equal(["chicken breast", "garlic", "olive oil", "basil"], names);

        // All four canonical names already exist in the seed, so no new rows.
        var after = await Client.GetJsonAsync<List<IngredientDto>>("/api/ingredients?limit=300");
        Assert.Equal(before.Count, after.Count);

        // The raw line the user typed is preserved for display.
        Assert.Equal("2 boneless, skinless chicken breasts, diced", recipe.Ingredients[0].RawText);
    }

    [Fact]
    public async Task Cuisines_and_tags_are_matched_case_insensitively()
    {
        var token = await Client.RegisterAsync("cook");
        var before = await Client.GetJsonAsync<List<CuisineDto>>("/api/cuisines");

        var id = await CreateAsync(token, Api.NewRecipe(cuisines: ["italian"], tags: ["VEGAN"]));

        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");
        var after = await Client.GetJsonAsync<List<CuisineDto>>("/api/cuisines");

        // Reuses the seeded "Italian" row, keeping its original casing.
        Assert.Contains("Italian", recipe.Cuisines);
        Assert.Equal(before.Count, after.Count);
        Assert.Single(after, c => string.Equals(c.Name, "italian", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_newly_created_recipe_is_immediately_searchable()
    {
        var token = await Client.RegisterAsync("cook");
        var title = $"Findable {Guid.CreateVersion7():N}";
        await CreateAsync(token, Api.NewRecipe(title: title, cuisines: ["Testish"]));

        var byTitle = await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>(
            $"/api/recipes?search={Uri.EscapeDataString(title)}");
        var byCuisine = await Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>(
            "/api/recipes?cuisines=Testish&pageSize=50");

        Assert.Equal(title, Assert.Single(byTitle.Items).Title);
        Assert.Equal(title, Assert.Single(byCuisine.Items).Title);
    }

    // --------------------------------------------------------- validation

    public static TheoryData<string, CreateRecipeRequest> InvalidRecipes => new()
    {
        { "no ingredients", Api.NewRecipe(ingredients: []) },
        { "no steps", Api.NewRecipe(steps: []) },
        { "blank title", Api.NewRecipe(title: "") },
        { "zero servings", Api.NewRecipe(servings: 0) },
        { "negative prep time", Api.NewRecipe(prep: -1) }
    };

    [Theory]
    [MemberData(nameof(InvalidRecipes))]
    public async Task Invalid_payloads_are_rejected(string _, CreateRecipeRequest request)
    {
        var token = await Client.RegisterAsync("cook");

        using var response = await Client.SendAsync(HttpMethod.Post, "/api/recipes", request, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
