using RecipeApi.Application.Services;

namespace RecipeApi.IntegrationTests.Unit;

/// <summary>
/// Every case below is a label delish.com actually publishes. Before this
/// existed the cuisine filter listed "American", "American Cuisine" and
/// "American (US) Cuisine" side by side, each returning a different subset —
/// three ways to ask one question, none of them complete.
/// </summary>
public class CuisineNameTests
{
    [Theory]
    [InlineData("American", "American")]
    [InlineData("American Cuisine", "American")]
    [InlineData("American (US) Cuisine", "American")]
    [InlineData("Italian Cuisine", "Italian")]
    [InlineData("Latin American Cuisine", "Latin American")]
    [InlineData("  French   Cuisine  ", "French")]
    // Case is preserved: these stay display text, unlike ingredient names.
    [InlineData("Thai", "Thai")]
    // Hyphen variants have to collapse: delish.com puts both of these on the
    // same recipe, which produced two rows for one thing.
    [InlineData("Nut-Free Diet", "Nut Free Diet")]
    [InlineData("Nut Free Diet", "Nut Free Diet")]
    [InlineData("Tex-Mex", "Tex Mex")]
    public void Collapses_the_ways_a_cuisine_gets_written(string raw, string expected) =>
        Assert.Equal(expected, CuisineName.Normalize(raw));

    /// <summary>
    /// "Cuisine" is only stripped as a suffix. A name that merely contains the
    /// word, or is only the word, has to survive — returning an empty string
    /// would drop the label entirely.
    /// </summary>
    [Theory]
    [InlineData("Cuisine", "Cuisine")]
    [InlineData("Cuisine Bourgeoise", "Cuisine Bourgeoise")]
    public void Only_strips_a_trailing_cuisine_word(string raw, string expected) =>
        Assert.Equal(expected, CuisineName.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_yields_an_empty_name(string? raw) =>
        Assert.Equal(string.Empty, CuisineName.Normalize(raw));

    /// <summary>Normalizing an already-clean label must not change it.</summary>
    [Theory]
    [InlineData("American")]
    [InlineData("Latin American")]
    [InlineData("Middle Eastern")]
    public void Is_idempotent(string canonical) =>
        Assert.Equal(canonical, CuisineName.Normalize(canonical));
}
