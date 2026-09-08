using RecipeApi.Infrastructure.Scraping;

namespace RecipeApi.IntegrationTests.Unit;

/// <summary>
/// Hand-rolled rather than XmlConvert.ToTimeSpan because real pages carry values
/// that are close to ISO 8601 but not quite, and a throwing parser mid-scrape is
/// worse than one that returns null and lets the caller move on.
/// </summary>
public class IsoDurationParserTests
{
    [Theory]
    [InlineData("PT1H30M", 90)]
    [InlineData("PT45M", 45)]
    [InlineData("PT1H", 60)]
    [InlineData("PT2H15M", 135)]
    [InlineData("P0DT0H30M", 30)]
    [InlineData("P1DT2H", 1560)]
    [InlineData("pt1h30m", 90)]        // case-insensitive
    [InlineData("PT90M", 90)]
    [InlineData("PT30S", null)]        // rounds below a minute
    public void Parses_iso_8601_durations(string value, int? expected) =>
        Assert.Equal(expected, IsoDurationParser.ToMinutes(value));

    [Theory]
    [InlineData("45 minutes", 45)]
    [InlineData("1 hr 30 min", 90)]
    [InlineData("2 hours", 120)]
    [InlineData("90 mins", 90)]
    public void Parses_the_loose_forms_sites_actually_publish(string value, int expected) =>
        Assert.Equal(expected, IsoDurationParser.ToMinutes(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PT")]                 // syntactically valid, carries no value
    [InlineData("overnight")]
    [InlineData("until done")]
    public void Returns_null_rather_than_throwing_on_unusable_input(string? value) =>
        Assert.Null(IsoDurationParser.ToMinutes(value));
}
