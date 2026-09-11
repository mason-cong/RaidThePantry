using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RecipeApi.IntegrationTests.Fixtures;

/// <summary>
/// A real HTTP server on loopback, serving the JSON-LD shapes that appear in the
/// wild plus the failure cases the importer must reject.
///
/// The importer is only meaningful end to end — it fetches, follows redirects,
/// enforces a size ceiling and re-checks the SSRF guard at each hop. Stubbing
/// HttpClient would skip exactly the parts worth testing.
/// </summary>
public sealed class FixtureSite : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }

    private FixtureSite(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public string Url(string path) => $"{BaseUrl}{path}";

    public static async Task<FixtureSite> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");   // port 0: let the OS choose

        var app = builder.Build();
        MapRoutes(app);

        await app.StartAsync();

        var address = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new FixtureSite(app, address.TrimEnd('/'));
    }

    private static void MapRoutes(WebApplication app)
    {
        app.MapGet("/simple", () => Html(SimpleRecipe));
        app.MapGet("/graph", () => Html(GraphRecipe));
        app.MapGet("/sections", () => Html(SectionsRecipe));
        app.MapGet("/blob", () => Html(BlobRecipe));

        // The first block is unparseable; the extractor must skip it and go on.
        app.MapGet("/malformed-then-good", () => Html("{ this is not json ]", SimpleRecipe));

        app.MapGet("/no-recipe", () => Html("""
            {"@context":"https://schema.org","@type":"WebPage","name":"Just a page"}
            """));

        app.MapGet("/no-jsonld", () => Results.Content(
            "<!doctype html><html><body><h1>Nothing structured</h1></body></html>", "text/html"));

        // Passes the @type check but has no ingredients or steps, so it is unusable.
        app.MapGet("/incomplete", () => Html("""
            {"@context":"https://schema.org","@type":"Recipe","name":"Name Only"}
            """));

        app.MapGet("/not-html", () => Results.Json(new { hello = "world" }));

        app.MapGet("/redirect", () => Results.Redirect("/simple"));
        app.MapGet("/redirect-chain", () => Results.Redirect("/redirect"));
        app.MapGet("/redirect-loop", () => Results.Redirect("/redirect-loop"));

        // A public-looking URL bouncing to cloud metadata: the case automatic
        // redirect following would walk straight into.
        app.MapGet("/redirect-to-metadata",
            () => Results.Redirect("http://169.254.169.254/latest/meta-data/"));

        app.MapGet("/boom", () => Results.StatusCode(StatusCodes.Status500InternalServerError));

        // For the Worker's crawl. The named group has to win over the wildcard,
        // so the wildcard here is deliberately *more* permissive — if the
        // checker were picking the wrong group, /blocked/ would be allowed and
        // the test would notice.
        app.MapGet("/robots.txt", (HttpContext ctx) => Results.Content($"""
            Sitemap: {Origin(ctx)}/sitemap.xml
            Sitemap: https://elsewhere.invalid/sitemap.xml

            User-agent: *
            Disallow: /nothing-in-particular/

            User-agent: RecipeFinderBot
            Disallow: /blocked/
            Crawl-delay: 0
            """, "text/plain"));

        // An index, so discovery has to recurse rather than read one document.
        app.MapGet("/sitemap.xml", (HttpContext ctx) => Results.Content($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap><loc>{Origin(ctx)}/sitemap_1.xml</loc></sitemap>
            </sitemapindex>
            """, "application/xml"));

        app.MapGet("/sitemap_1.xml", (HttpContext ctx) => Results.Content($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>{Origin(ctx)}/recipe/one</loc></url>
              <url><loc>{Origin(ctx)}/recipe/two</loc></url>
              <url><loc>{Origin(ctx)}/recipe/three</loc></url>
              <url><loc>{Origin(ctx)}/article/not-a-recipe</loc></url>
              <url><loc>{Origin(ctx)}/blocked/secret-recipe</loc></url>
              <url><loc>https://elsewhere.invalid/recipe/off-site</loc></url>
            </urlset>
            """, "application/xml"));

        // A perfectly good recipe that robots.txt puts off limits. Serving a
        // real one matters: if this 404'd, a test asserting it was not staged
        // would pass even with robots.txt handling removed entirely.
        app.MapGet("/blocked/secret-recipe", () => Html(SimpleRecipe));

        // Comfortably past the 2MB ceiling.
        app.MapGet("/huge", () => HtmlWithPadding(
            string.Concat(Enumerable.Repeat("<p>padding padding padding</p>", 120_000)),
            SimpleRecipe));
    }

    /// <summary>The port is chosen by the OS, so sitemap URLs have to be built at request time.</summary>
    private static string Origin(HttpContext context) =>
        $"{context.Request.Scheme}://{context.Request.Host}";

    private static IResult Html(params string[] jsonLdBlocks) =>
        HtmlWithPadding(string.Empty, jsonLdBlocks);

    // Deliberately not an overload of Html. A `(string, string)` overload wins
    // against `params string[]` for a two-argument call, which silently turned
    // Html(malformed, recipe) into one block plus page padding.
    private static IResult HtmlWithPadding(string padding, params string[] jsonLdBlocks)
    {
        var scripts = string.Concat(jsonLdBlocks.Select(b =>
            $"""<script type="application/ld+json">{b}</script>"""));

        return Results.Content(
            $"<!doctype html><html><head><title>Fixture</title>{scripts}</head><body>{padding}</body></html>",
            "text/html; charset=utf-8");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ------------------------------------------------------------- payloads

    /// <summary>Flat Recipe, ISO durations, keywords as one comma-separated string.</summary>
    public const string SimpleRecipe = """
        {
          "@context": "https://schema.org",
          "@type": "Recipe",
          "name": "Fixture Lemon Chicken",
          "description": "A bright, fast weeknight chicken.",
          "image": "https://example.com/lemon-chicken.jpg",
          "prepTime": "PT15M",
          "cookTime": "PT35M",
          "recipeYield": "4 servings",
          "recipeCuisine": "Mediterranean",
          "keywords": "quick, chicken, weeknight",
          "recipeIngredient": [
            "4 boneless, skinless chicken thighs",
            "2 lemons, zested and juiced",
            "3 cloves garlic, minced",
            "2 tbsp olive oil"
          ],
          "recipeInstructions": [
            {"@type": "HowToStep", "text": "Marinate the chicken."},
            {"@type": "HowToStep", "text": "Sear skin-side down."},
            {"@type": "HowToStep", "text": "Finish in the oven."}
          ]
        }
        """;

    /// <summary>Nested in @graph, @type as an array, image as an ImageObject, totalTime only.</summary>
    public const string GraphRecipe = """
        {
          "@context": "https://schema.org",
          "@graph": [
            {"@type": "WebSite", "name": "Fixture Food"},
            {
              "@type": ["Recipe", "NewsArticle"],
              "name": "Fixture Graph Curry",
              "image": {"@type": "ImageObject", "url": "https://example.com/curry.jpg"},
              "totalTime": "PT1H10M",
              "recipeYield": ["6", "6 servings"],
              "recipeCuisine": ["Indian", "British"],
              "keywords": ["curry", "batch-cooking"],
              "recipeIngredient": ["500g chicken breast, diced", "2 onions, finely sliced"],
              "recipeInstructions": ["Brown the onions.", "Add spices.", "Simmer."]
            }
          ]
        }
        """;

    /// <summary>Instructions as HowToSection wrapping nested HowToStep lists; yield as a number.</summary>
    public const string SectionsRecipe = """
        {
          "@context": "https://schema.org",
          "@type": "Recipe",
          "name": "Fixture Sectioned Cake",
          "prepTime": "PT30M",
          "cookTime": "PT45M",
          "recipeYield": 8,
          "recipeIngredient": ["200g plain flour", "4 eggs"],
          "recipeInstructions": [
            {
              "@type": "HowToSection",
              "name": "Batter",
              "itemListElement": [
                {"@type": "HowToStep", "text": "Cream the butter and sugar."},
                {"@type": "HowToStep", "text": "Beat in the eggs."}
              ]
            },
            {
              "@type": "HowToSection",
              "name": "Bake",
              "itemListElement": [
                {"@type": "HowToStep", "text": "Fold in the flour."},
                {"@type": "HowToStep", "text": "Bake at 180C."}
              ]
            }
          ]
        }
        """;

    /// <summary>Instructions as one HTML blob, and a non-ISO duration.</summary>
    public const string BlobRecipe = """
        {
          "@context": "https://schema.org",
          "@type": "Recipe",
          "name": "Fixture Blob Soup",
          "totalTime": "45 minutes",
          "recipeYield": "Serves 4",
          "recipeIngredient": ["1 kg carrots, peeled", "1 litre vegetable stock"],
          "recipeInstructions": "<ol><li>Sweat the onion.</li><li>Add carrots.</li><li>Blend.</li></ol>"
        }
        """;
}
