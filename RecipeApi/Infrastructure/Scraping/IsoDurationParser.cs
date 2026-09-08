using System.Text.RegularExpressions;

namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// schema.org durations are ISO 8601: "PT1H30M" is 90 minutes.
///
/// Hand-rolled rather than XmlConvert.ToTimeSpan because real pages carry values
/// that are close to the standard but not quite ("PT90M" is fine, "90 min" and
/// "PT" alone are not), and a throwing parser in the middle of a scrape is worse
/// than one that returns null and lets the caller move on.
/// </summary>
public static partial class IsoDurationParser
{
    public static int? ToMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();

        var match = IsoDuration().Match(text);
        if (match.Success && match.Groups.Cast<Group>().Skip(1).Any(g => g.Success))
        {
            var weeks = Number(match, "w");
            var days = Number(match, "d");
            var hours = Number(match, "h");
            var minutes = Number(match, "m");
            var seconds = Number(match, "s");

            var total = weeks * 7 * 24 * 60 + days * 24 * 60 + hours * 60 + minutes + seconds / 60;

            // Checked after rounding, not before: "PT30S" is 0.5 minutes, which
            // passes a `total > 0` test and then rounds to 0 — reporting a known
            // zero rather than "no usable duration".
            var rounded = (int)Math.Round(total);
            return rounded > 0 ? rounded : null;
        }

        // "45 minutes", "1 hr 30 min" — common enough in the wild to be worth catching.
        var loose = LooseDuration().Matches(text);
        if (loose.Count > 0)
        {
            double total = 0;
            foreach (Match m in loose)
            {
                var amount = double.Parse(m.Groups["n"].Value);
                var unit = m.Groups["u"].Value.ToLowerInvariant();
                total += unit.StartsWith('h') ? amount * 60 : amount;
            }
            return total > 0 ? (int)Math.Round(total) : null;
        }

        return null;
    }

    private static double Number(Match match, string group) =>
        match.Groups[group].Success ? double.Parse(match.Groups[group].Value) : 0;

    [GeneratedRegex(@"^P(?:(?<w>\d+(?:\.\d+)?)W)?(?:(?<d>\d+(?:\.\d+)?)D)?(?:T(?:(?<h>\d+(?:\.\d+)?)H)?(?:(?<m>\d+(?:\.\d+)?)M)?(?:(?<s>\d+(?:\.\d+)?)S)?)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex IsoDuration();

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?<u>hours?|hrs?|h|minutes?|mins?|m)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LooseDuration();
}
