using System.Net;
using RecipeApi.Application.Dtos;
using RecipeApi.Domain;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests;

public class ImportTests(ApiFixture fixture) : ApiTestBase(fixture), IAsyncDisposable
{
    private FixtureSite? _site;

    private async Task<FixtureSite> SiteAsync() => _site ??= await FixtureSite.StartAsync();

    private Task<HttpResponseMessage> ImportAsync(string url, string? token) =>
        Client.SendAsync(HttpMethod.Post, "/api/import/url", new { url }, token);

    private async Task<Guid> ImportOkAsync(string url, string token)
    {
        using var response = await ImportAsync(url, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<Guid>();
    }

    // ------------------------------------------------------------- gating

    [Fact]
    public async Task Importing_requires_an_account()
    {
        var site = await SiteAsync();

        using var response = await ImportAsync(site.Url("/simple"), token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --------------------------------------------------------- extraction

    [Fact]
    public async Task A_flat_json_ld_recipe_is_imported_whole()
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        var id = await ImportOkAsync(site.Url("/simple"), token);
        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}", token);

        Assert.Equal("Fixture Lemon Chicken", recipe.Title);
        Assert.Equal(15, recipe.PrepTimeMinutes);
        Assert.Equal(35, recipe.CookTimeMinutes);
        Assert.Equal(4, recipe.Servings);
        Assert.Equal(RecipeSourceType.UrlImport, recipe.SourceType);
        Assert.Equal(site.Url("/simple"), recipe.SourceUrl);
        Assert.Contains("Mediterranean", recipe.Cuisines);
        // Alphabetical: cuisines and tags are returned in a defined order.
        Assert.Equal(["chicken", "quick", "weeknight"], recipe.Tags);
        Assert.Equal(3, recipe.Steps.Count);

        // Imported ingredients go through the same normalizer as manual ones.
        Assert.Equal("chicken thigh", recipe.Ingredients[0].Name);
        Assert.Equal("4 boneless, skinless chicken thighs", recipe.Ingredients[0].RawText);
        Assert.Contains(recipe.Ingredients, i => i.Name == "garlic");
        Assert.Contains(recipe.Ingredients, i => i.Name == "olive oil");

        // The importer owns nothing: the caller does.
        Assert.True(recipe.IsEditable);
    }

    [Fact]
    public async Task A_recipe_nested_in_a_graph_with_an_array_type_is_found()
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        var id = await ImportOkAsync(site.Url("/graph"), token);
        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");

        Assert.Equal("Fixture Graph Curry", recipe.Title);
        // totalTime PT1H10M with no prepTime: the whole 70 minutes lands on cook.
        Assert.Equal(0, recipe.PrepTimeMinutes);
        Assert.Equal(70, recipe.CookTimeMinutes);
        Assert.Equal(6, recipe.Servings);                            // from ["6", "6 servings"]
        Assert.Equal("https://example.com/curry.jpg", recipe.ImageUrl);   // from ImageObject.url
        Assert.Equal(2, recipe.Cuisines.Count);
    }

    [Fact]
    public async Task How_to_sections_are_flattened_into_ordered_steps()
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        var id = await ImportOkAsync(site.Url("/sections"), token);
        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");

        Assert.Equal(
            ["Cream the butter and sugar.", "Beat in the eggs.", "Fold in the flour.", "Bake at 180C."],
            recipe.Steps);
        Assert.Equal(8, recipe.Servings);   // recipeYield as a bare number
    }

    [Fact]
    public async Task A_single_html_blob_of_instructions_is_split_into_steps()
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        var id = await ImportOkAsync(site.Url("/blob"), token);
        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");

        Assert.Equal(["Sweat the onion.", "Add carrots.", "Blend."], recipe.Steps);
        Assert.Equal(45, recipe.CookTimeMinutes);   // "45 minutes", not an ISO duration
        Assert.Equal(4, recipe.Servings);           // "Serves 4"
    }

    [Fact]
    public async Task A_malformed_json_ld_block_does_not_abandon_the_page()
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        var id = await ImportOkAsync(site.Url("/malformed-then-good"), token);
        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");

        Assert.Equal("Fixture Lemon Chicken", recipe.Title);
    }

    [Fact]
    public async Task Redirects_are_followed()
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        var id = await ImportOkAsync(site.Url("/redirect"), token);
        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");

        Assert.Equal("Fixture Lemon Chicken", recipe.Title);
        // Recorded against the URL asked for, which is what dedupes repeat clicks.
        Assert.Equal(site.Url("/redirect"), recipe.SourceUrl);
    }

    // -------------------------------------------------------- duplication

    [Fact]
    public async Task Importing_the_same_url_twice_returns_the_existing_recipe()
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        var first = await ImportOkAsync(site.Url("/simple"), token);

        using var second = await ImportAsync(site.Url("/simple"), token);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains(first.ToString(), body);
    }

    // ---------------------------------------------------------- SSRF guard
    // Without this, the endpoint is a request-forgery tool: any account could
    // read cloud instance credentials back out of an "imported recipe".

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/", "cloud metadata")]
    [InlineData("http://10.0.0.1/admin", "private 10/8")]
    [InlineData("http://192.168.1.1/", "private 192.168/16")]
    [InlineData("http://172.16.0.5/", "private 172.16/12")]
    [InlineData("http://100.64.0.1/", "carrier-grade NAT")]
    [InlineData("http://[fc00::1]/", "IPv6 unique local")]
    [InlineData("http://0.0.0.0/", "unspecified address")]
    public async Task Internal_hosts_are_refused(string url, string _)
    {
        var token = await Client.RegisterAsync("importer");

        using var response = await ImportAsync(url, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("file:///etc/passwd", "file scheme")]
    [InlineData("ftp://example.com/x", "ftp scheme")]
    [InlineData("gopher://example.com/", "gopher scheme")]
    [InlineData("http://user:pass@example.com/", "embedded credentials")]
    [InlineData("just some text", "not a URL")]
    [InlineData("/relative/path", "relative URL")]
    public async Task Unacceptable_url_forms_are_refused(string url, string _)
    {
        var token = await Client.RegisterAsync("importer");

        using var response = await ImportAsync(url, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The reason redirects are followed by hand. Automatic redirect following
    /// would re-point the request after the host check had already passed.
    /// </summary>
    [Fact]
    public async Task A_redirect_into_cloud_metadata_is_refused()
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        using var response = await ImportAsync(site.Url("/redirect-to-metadata"), token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------ failures
    // 400 for a URL this API refuses, 422 for a page that fetched fine but is not
    // a recipe, 502 when the upstream site is at fault.

    [Theory]
    [InlineData("/no-recipe", "valid JSON-LD that is not a Recipe")]
    [InlineData("/no-jsonld", "no structured data at all")]
    [InlineData("/incomplete", "a Recipe with no ingredients or steps")]
    [InlineData("/not-html", "a JSON response")]
    public async Task Pages_without_a_usable_recipe_are_422(string path, string _)
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        using var response = await ImportAsync(site.Url(path), token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("/boom", "upstream 500")]
    [InlineData("/missing", "upstream 404")]
    [InlineData("/huge", "over the response size ceiling")]
    [InlineData("/redirect-loop", "too many redirects")]
    public async Task Upstream_failures_are_502(string path, string _)
    {
        var site = await SiteAsync();
        var token = await Client.RegisterAsync("importer");

        using var response = await ImportAsync(site.Url(path), token);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_site is not null)
            await _site.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
