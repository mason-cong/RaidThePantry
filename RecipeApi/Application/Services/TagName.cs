using System.Text.RegularExpressions;

namespace RecipeApi.Application.Services;

/// <summary>
/// Decides whether a scraped keyword is a tag at all, and what it should read as.
///
/// schema.org's `keywords` is free text, and publishers use it as a dumping
/// ground for their CMS. delish.com emits entries like
/// "contentId: d05c0f11-b8f4-414d-812b-f941ddacdd04", "isSyndicated: false" and
/// "shortTitle: PSL Tiramisu" alongside genuine tags — 162 of 322 rows in the
/// tag table were shaped like that, and every one of them was rendered to
/// visitors as a badge. A database key from somebody else's CMS is not a tag.
///
/// So anything shaped `key: value` is discarded, with one exception:
/// "collection: Desserts" carries a real tag behind a meaningless prefix, and
/// the value is kept.
/// </summary>
public static partial class TagName
{
    /// <summary>
    /// Prefixes whose *value* is a usable tag. Everything else in `key: value`
    /// form is metadata and goes. Add to this list rather than loosening the
    /// rule — the default of dropping is what keeps the badges clean.
    /// </summary>
    private static readonly HashSet<string> MeaningfulPrefixes =
        new(StringComparer.OrdinalIgnoreCase) { "collection" };

    /// <returns>The tag to store, or <c>null</c> when it should be dropped.</returns>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var cleaned = Whitespace().Replace(raw, " ").Trim();

        var pair = KeyValuePair().Match(cleaned);
        if (pair.Success)
        {
            if (!MeaningfulPrefixes.Contains(pair.Groups["key"].Value))
                return null;

            // Recurse: the value could itself be prefixed, and it still has to
            // pass the emptiness check.
            return Normalize(pair.Groups["value"].Value);
        }

        // A tag long enough to be a sentence is a description, not a label.
        return cleaned.Length is 0 or > 60 ? null : cleaned;
    }

    // The key must start with a letter, so a time like "5:00" is not mistaken
    // for a metadata prefix.
    [GeneratedRegex(@"^(?<key>[A-Za-z][A-Za-z0-9_-]*)\s*:\s*(?<value>.+)$")]
    private static partial Regex KeyValuePair();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
