using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// Finds a schema.org/Recipe in a page's JSON-LD blocks and flattens it.
///
/// Almost every field is polymorphic in the wild, so this navigates JsonElement
/// by hand rather than deserializing into a POCO: a typed model would need a
/// custom converter per field and would still throw on the first site that
/// nests things one level deeper than expected. A malformed block is skipped,
/// not fatal — pages routinely carry several, only one of which is the recipe.
/// </summary>
public partial class JsonLdRecipeExtractor
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly HtmlParser _parser = new();

    public SchemaOrgRecipe? Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var document = _parser.ParseDocument(html);

        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            var json = script.TextContent;
            if (string.IsNullOrWhiteSpace(json))
                continue;

            try
            {
                using var parsed = JsonDocument.Parse(json, JsonOptions);

                if (FindRecipeNode(parsed.RootElement, depth: 0) is not JsonElement node)
                    continue;

                // Mapped inside the using: a JsonElement does not outlive its document.
                var recipe = Map(node);
                if (recipe.IsUsable)
                    return recipe;
            }
            catch (JsonException)
            {
                // One unparseable block should not abandon the page.
            }
        }

        return null;
    }

    private static JsonElement? FindRecipeNode(JsonElement node, int depth)
    {
        if (depth > 6)
            return null;

        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                    if (FindRecipeNode(item, depth + 1) is JsonElement found)
                        return found;
                return null;

            case JsonValueKind.Object:
                if (HasType(node, "Recipe"))
                    return node;

                // Recipes commonly hang off @graph, or off mainEntity when the
                // page describes itself as a WebPage/Article first.
                foreach (var key in new[] { "@graph", "mainEntity", "mainEntityOfPage", "itemListElement" })
                    if (node.TryGetProperty(key, out var child) &&
                        FindRecipeNode(child, depth + 1) is JsonElement found)
                        return found;

                return null;

            default:
                return null;
        }
    }

    private static bool HasType(JsonElement node, string type)
    {
        if (!node.TryGetProperty("@type", out var typeNode))
            return false;

        return typeNode.ValueKind switch
        {
            JsonValueKind.String => string.Equals(typeNode.GetString(), type, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => typeNode.EnumerateArray().Any(t =>
                t.ValueKind == JsonValueKind.String &&
                string.Equals(t.GetString(), type, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static SchemaOrgRecipe Map(JsonElement node)
    {
        var recipe = new SchemaOrgRecipe
        {
            Name = Clean(FirstString(node, "name") ?? FirstString(node, "headline")),
            Description = Clean(FirstString(node, "description")),
            ImageUrl = FirstString(node, "image"),
            PrepTimeMinutes = IsoDurationParser.ToMinutes(FirstString(node, "prepTime")),
            CookTimeMinutes = IsoDurationParser.ToMinutes(FirstString(node, "cookTime")),
            TotalTimeMinutes = IsoDurationParser.ToMinutes(FirstString(node, "totalTime")),
            Servings = ParseServings(node)
        };

        if (node.TryGetProperty("recipeIngredient", out var ingredients) ||
            node.TryGetProperty("ingredients", out ingredients))
        {
            recipe.Ingredients = Strings(ingredients).Select(Clean).Where(NotEmpty).ToList()!;
        }

        if (node.TryGetProperty("recipeInstructions", out var instructions))
            recipe.Steps = ExtractSteps(instructions, depth: 0);

        if (node.TryGetProperty("recipeCuisine", out var cuisine))
            recipe.Cuisines = Strings(cuisine).Select(Clean).Where(NotEmpty).ToList()!;

        var keywords = new List<string>();
        if (node.TryGetProperty("keywords", out var kw))
            keywords.AddRange(Strings(kw));
        if (node.TryGetProperty("recipeCategory", out var category))
            keywords.AddRange(Strings(category));

        recipe.Keywords = keywords
            // "keywords" is frequently one comma-separated string rather than a list.
            .SelectMany(k => k.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(Clean)
            .Where(NotEmpty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;

        return recipe;
    }

    /// <summary>
    /// Instructions are the messiest field: a plain string of HTML, an array of
    /// strings, an array of HowToStep objects with "text", or an array of
    /// HowToSection each wrapping its own itemListElement of steps.
    /// </summary>
    private static List<string> ExtractSteps(JsonElement node, int depth)
    {
        if (depth > 4)
            return [];

        var steps = new List<string>();

        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                // One blob: split on line breaks, or on <li>/<p> if it is HTML.
                var text = node.GetString() ?? string.Empty;
                steps.AddRange(BlockSplit()
                    .Split(text)
                    .Select(Clean)
                    .Where(NotEmpty)!);
                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                    steps.AddRange(ExtractSteps(item, depth + 1));
                break;

            case JsonValueKind.Object:
                if (node.TryGetProperty("itemListElement", out var nested))
                {
                    steps.AddRange(ExtractSteps(nested, depth + 1));
                }
                else
                {
                    var value = Clean(FirstString(node, "text") ?? FirstString(node, "name"));
                    if (NotEmpty(value))
                        steps.Add(value!);
                }
                break;
        }

        return steps;
    }

    private static int? ParseServings(JsonElement node)
    {
        if (!node.TryGetProperty("recipeYield", out var yieldNode))
            return null;

        foreach (var candidate in Strings(yieldNode))
        {
            var digits = Digits().Match(candidate);
            if (digits.Success && int.TryParse(digits.Value, out var parsed) && parsed is > 0 and <= 1000)
                return parsed;
        }

        return null;
    }

    /// <summary>Flattens string | number | object | array-of-those into plain strings.</summary>
    private static IEnumerable<string> Strings(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                var s = node.GetString();
                if (!string.IsNullOrWhiteSpace(s)) yield return s;
                break;

            case JsonValueKind.Number:
                yield return node.GetRawText();
                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                    foreach (var value in Strings(item))
                        yield return value;
                break;

            case JsonValueKind.Object:
                // {"@type":"ImageObject","url":"..."} and friends.
                foreach (var key in new[] { "url", "text", "name", "@id" })
                    if (node.TryGetProperty(key, out var inner) && inner.ValueKind == JsonValueKind.String)
                    {
                        var value = inner.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) yield return value;
                        break;
                    }
                break;
        }
    }

    private static string? FirstString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) ? Strings(value).FirstOrDefault() : null;

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = WebUtility.HtmlDecode(Tags().Replace(value, " "));
        return Whitespace().Replace(text, " ").Trim();
    }

    private static bool NotEmpty(string? value) => !string.IsNullOrWhiteSpace(value);

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    // Split a single instruction blob on list/paragraph boundaries or newlines.
    [GeneratedRegex(@"</li>|</p>|<br\s*/?>|\r?\n", RegexOptions.IgnoreCase)]
    private static partial Regex BlockSplit();
}
