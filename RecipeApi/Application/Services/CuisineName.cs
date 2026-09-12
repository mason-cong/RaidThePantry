using System.Text.RegularExpressions;

namespace RecipeApi.Application.Services;

/// <summary>
/// Tidies a cuisine label before it becomes a row.
///
/// Unlike ingredients these stay display text — "Italian", not "italian" — so
/// this is a light touch rather than the full tokenizing treatment. It exists
/// because real schema.org data is inconsistent about the same cuisine:
/// delish.com alone publishes "American", "American Cuisine" and
/// "American (US) Cuisine", which case-insensitive matching still stores as
/// three separate rows. The filter list then offers the visitor three ways to
/// ask the same question, each returning a different subset.
///
/// Deliberately not applied to tags. A tag is free-form by design, and
/// "Comfort Food" means something there.
/// </summary>
public static partial class CuisineName
{
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // "American (US) Cuisine" -> "American  Cuisine"
        var cleaned = Parentheticals().Replace(raw, " ");

        // "Italian Cuisine" -> "Italian". Only as a suffix: a cuisine genuinely
        // called something ending in another word keeps it.
        cleaned = TrailingCuisineWord().Replace(cleaned, string.Empty);

        // Hyphens become spaces, so "Nut-Free Diet" and "Nut Free Diet" — which
        // delish.com publishes on the same recipe — reach one row instead of
        // two. Also merges "Tex-Mex" with "Tex Mex".
        cleaned = cleaned.Replace('-', ' ').Replace('–', ' ');

        cleaned = Whitespace().Replace(cleaned, " ").Trim(' ', ',');

        // If stripping left nothing — a label that was only "Cuisine" — the
        // original is better than an empty string.
        return cleaned.Length == 0 ? raw.Trim() : cleaned;
    }

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parentheticals();

    [GeneratedRegex(@"\s+cuisine\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingCuisineWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
