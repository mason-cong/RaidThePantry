using System.Text.RegularExpressions;
using RecipeApi.Application.Interfaces;

namespace RecipeApi.Application.Services;

/// <summary>
/// v1: regex and stopword cleanup. Lives in Application rather than Infrastructure
/// because it is pure string logic with no external dependency — it needs no
/// database, no HTTP, and is directly unit-testable.
///
/// Known limits, accepted for v1: it cannot tell "sun-dried tomato" (keep the
/// qualifier, it is a distinct ingredient) from "dried oregano" (drop it, same as
/// oregano) beyond treating hyphenated words as single tokens. Plural handling is
/// suffix-based, so irregulars ("leaves" -> "leave") are wrong. A lookup table in
/// v2 fixes both; until there is real ingredient volume there is nothing to build
/// that table from.
/// </summary>
public partial class IngredientNormalizer : IIngredientNormalizer
{
    public string Normalize(string rawIngredientText)
    {
        if (string.IsNullOrWhiteSpace(rawIngredientText))
            return string.Empty;

        var text = rawIngredientText.ToLowerInvariant().Trim();

        // "1 tin (400g) chopped tomatoes" -> "1 tin  chopped tomatoes"
        text = Parentheticals().Replace(text, " ");

        // Unicode fraction glyphs, then quantities/ranges: "2 1/2", "1-2", "1.5".
        text = Fractions().Replace(text, " ");
        text = Quantities().Replace(text, " ");

        // Pick the first comma-separated segment that carries a content word.
        //
        // Truncating at the first comma unconditionally is wrong: in
        // "2 boneless, skinless chicken breasts, diced" the leading segment is
        // nothing but a descriptor, and cutting there yields "boneless". Skipping
        // content-free segments finds "skinless chicken breasts" instead, while
        // still discarding the trailing "diced" that a plain comma-strip keeps.
        var tokens = text
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Tokenize)
            .FirstOrDefault(segment => segment.Any(IsContentWord))
            ?? Tokenize(text.Replace(',', ' '));

        // "sun-dried tomatoes in oil" -> "sun-dried tomatoes". A trailing "in X"
        // names the packing medium, not the ingredient. This also flattens
        // "chipotle in adobo" to "chipotle", which is the price of the rule.
        var inIndex = tokens.IndexOf("in");
        if (inIndex > 0)
            tokens = tokens[..inIndex];

        // Units are only dropped from the front: "clove" leading is a measure
        // ("4 cloves garlic"), but "cloves" trailing is the spice.
        while (tokens.Count > 1 && (Units.Contains(tokens[0]) || Descriptors.Contains(tokens[0]) || Fillers.Contains(tokens[0])))
            tokens.RemoveAt(0);

        while (tokens.Count > 1 && Descriptors.Contains(tokens[^1]))
            tokens.RemoveAt(tokens.Count - 1);

        // Interior descriptors and fillers: "large free-range eggs" -> "eggs".
        var kept = tokens.Where(t => !Descriptors.Contains(t) && !Fillers.Contains(t)).ToList();
        if (kept.Count > 0)
            tokens = kept;

        if (tokens.Count == 0)
            return string.Empty;

        tokens[^1] = Singularize(tokens[^1]);

        return string.Join(' ', tokens);
    }

    private static List<string> Tokenize(string segment) => segment
        .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.Trim('.', ';', ':', '"', '\''))
        .Where(t => t.Length > 0)
        .ToList();

    /// <summary>A word that could name an ingredient, rather than measure or describe one.</summary>
    private static bool IsContentWord(string token) =>
        !Units.Contains(token) && !Descriptors.Contains(token) && !Fillers.Contains(token);

    private static string Singularize(string word)
    {
        if (word.Length <= 3 || word.EndsWith("ss") || word.EndsWith("us") || word.EndsWith("is"))
            return word;

        if (word.EndsWith("ies") && word.Length > 4)
            return string.Concat(word.AsSpan(0, word.Length - 3), "y");

        if (word.EndsWith("oes") || word.EndsWith("ches") || word.EndsWith("shes") || word.EndsWith("xes"))
            return word[..^2];

        return word.EndsWith('s') ? word[..^1] : word;
    }

    private static readonly HashSet<string> Units = new(StringComparer.Ordinal)
    {
        "g", "gram", "grams", "kg", "kilogram", "kilograms",
        "ml", "l", "litre", "litres", "liter", "liters",
        "oz", "ounce", "ounces", "lb", "lbs", "pound", "pounds",
        "cup", "cups", "tbsp", "tablespoon", "tablespoons",
        "tsp", "teaspoon", "teaspoons", "pinch", "pinches", "dash",
        "handful", "handfuls", "bunch", "bunches", "clove", "cloves",
        "slice", "slices", "sprig", "sprigs", "stick", "sticks",
        "tin", "tins", "can", "cans", "packet", "packets", "pack",
        "jar", "jars", "punnet", "head", "heads", "piece", "pieces",
        "x", "of"
    };

    private static readonly HashSet<string> Descriptors = new(StringComparer.Ordinal)
    {
        "fresh", "freshly", "dried", "frozen", "raw", "cooked", "ripe", "unripe",
        "large", "small", "medium", "extra", "jumbo", "baby",
        "chopped", "diced", "minced", "sliced", "grated", "shredded", "crushed",
        "ground", "toasted", "roasted", "peeled", "trimmed", "halved", "quartered",
        "boneless", "skinless", "seedless", "stoned", "pitted",
        "finely", "roughly", "thinly", "thickly", "coarsely", "lightly",
        "good", "quality", "optional", "taste",
        "free-range", "organic", "unsalted", "salted", "virgin", "cold",
        // "basil leaves" and "basil" are the same ingredient to a cook, and the
        // suffix singularizer would otherwise produce "leave".
        "leaf", "leaves"
    };

    /// <summary>
    /// Articles and connectives. Stripped from the front and the interior, but
    /// never allowed to make a segment look content-free.
    /// </summary>
    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "of", "and", "or", "plus", "more", "to",
        "about", "approximately", "some", "few"
    };

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parentheticals();

    [GeneratedRegex(@"[¼½¾⅓⅔⅛⅜⅝⅞]")]
    private static partial Regex Fractions();

    // Quantity at the START only: "2", "1-2", "1.5", "3/4", "2 1/2".
    //
    // Anchoring matters twice over. A \b-delimited pattern never matches "500g"
    // at all, because there is no word boundary between a digit and a letter.
    // And stripping every number in the string destroys digits that are part of
    // the name — "00 flour" becomes "flour", "70% dark chocolate" becomes
    // "% dark chocolate". Only the leading run is ever a measurement.
    [GeneratedRegex(@"^(\s*\d+(\.\d+)?(\s*[-–/]\s*\d+(\.\d+)?)?)+\s*")]
    private static partial Regex Quantities();
}
