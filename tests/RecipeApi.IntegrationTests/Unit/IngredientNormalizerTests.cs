using RecipeApi.Application.Services;

namespace RecipeApi.IntegrationTests.Unit;

/// <summary>
/// The normalizer decides whether two recipes share an ingredient, so a
/// regression here quietly breaks search rather than throwing. It is pure string
/// logic, so it is tested directly instead of through HTTP.
/// </summary>
public class IngredientNormalizerTests
{
    private readonly IngredientNormalizer _normalizer = new();

    [Theory]
    // Quantities, units and prep descriptors are stripped.
    [InlineData("4 cloves garlic, finely minced", "garlic")]
    [InlineData("1 tsp ground cumin", "cumin")]
    [InlineData("freshly ground black pepper", "black pepper")]
    [InlineData("3 large free-range eggs", "egg")]
    [InlineData("1/2 cup extra virgin olive oil", "olive oil")]
    [InlineData("2 1/2 cups plain flour", "plain flour")]
    [InlineData("1 tin (400g) chopped tomatoes", "tomato")]
    [InlineData("1.5 kg ripe plum tomatoes, halved", "plum tomato")]
    [InlineData("150ml double cream", "double cream")]
    [InlineData("2-3 tbsp dark brown sugar", "dark brown sugar")]
    // A trailing "in X" names the packing medium, not the ingredient.
    [InlineData("100g sun-dried tomatoes in oil, sliced", "sun-dried tomato")]
    public void Normalizes_real_ingredient_lines(string raw, string expected) =>
        Assert.Equal(expected, _normalizer.Normalize(raw));

    /// <summary>
    /// Regression: truncating at the first comma yielded "boneless", because the
    /// identity sits *after* the comma. The first segment carrying a content word
    /// is the one that matters.
    /// </summary>
    [Theory]
    [InlineData("2 boneless, skinless chicken breasts, diced", "chicken breast")]
    [InlineData("1 large onion, peeled and finely chopped", "onion")]
    public void Takes_the_first_comma_segment_that_names_something(string raw, string expected) =>
        Assert.Equal(expected, _normalizer.Normalize(raw));

    /// <summary>
    /// Regression: `\b\d+\b` never matches "500g" — there is no word boundary
    /// between a digit and a letter — and stripping every number instead destroys
    /// digits that are part of the name. Only the leading run is a measurement.
    /// </summary>
    [Theory]
    [InlineData("500g 00 flour, for the dough", "00 flour")]
    [InlineData("200g 70% dark chocolate, chopped", "70% dark chocolate")]
    [InlineData("300ml 2% milk", "2% milk")]
    public void Keeps_digits_that_belong_to_the_name(string raw, string expected) =>
        Assert.Equal(expected, _normalizer.Normalize(raw));

    /// <summary>
    /// Regression: a leading article stopped the unit-stripping loop, leaving
    /// "a handful of basil leave".
    /// </summary>
    [Theory]
    [InlineData("a handful of fresh basil leaves", "basil")]
    [InlineData("a pinch of salt", "salt")]
    public void Strips_leading_articles_and_measures(string raw, string expected) =>
        Assert.Equal(expected, _normalizer.Normalize(raw));

    /// <summary>
    /// The unique index on Ingredients.Name and the exact-match search path both
    /// depend on this. If casing ever leaks through, duplicates appear silently.
    /// </summary>
    [Theory]
    [InlineData("2 Boneless CHICKEN Breasts")]
    [InlineData("Extra Virgin Olive Oil")]
    [InlineData("SALT")]
    public void Always_returns_a_lowercase_name(string raw)
    {
        var result = _normalizer.Normalize(raw);

        Assert.Equal(result.ToLowerInvariant(), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_input_yields_an_empty_name(string? raw) =>
        Assert.Equal(string.Empty, _normalizer.Normalize(raw!));

    /// <summary>Normalizing an already-canonical name must not change it further.</summary>
    [Theory]
    [InlineData("chicken breast")]
    [InlineData("olive oil")]
    [InlineData("black pepper")]
    [InlineData("70% dark chocolate")]
    public void Is_idempotent(string canonical) =>
        Assert.Equal(canonical, _normalizer.Normalize(canonical));

    /// <summary>
    /// Single-letter unit abbreviations, taken verbatim from a real delish.com
    /// recipe. Before these were recognised, "4 c. cold heavy cream" normalized
    /// to "c heavy cream" — an ingredient that would never match any other
    /// recipe's "heavy cream", silently fragmenting the index the whole
    /// normalizer exists to keep whole.
    /// </summary>
    [Theory]
    [InlineData("4 c. cold heavy cream", "heavy cream")]
    [InlineData("2/3 c. (75 g) confectioners' sugar", "confectioners sugar")]
    // The same line as delish.com actually publishes it, with a curly apostrophe.
    // It has to reach the same name as the straight-quoted form above, or one
    // ingredient becomes two.
    [InlineData("2/3 c. (75 g) confectioners’ sugar", "confectioners sugar")]
    [InlineData("1 T. unsalted butter", "butter")]
    [InlineData("2 t. vanilla extract", "vanilla extract")]
    [InlineData("1 pt. heavy cream", "heavy cream")]
    [InlineData("2 qt. chicken stock", "chicken stock")]
    [InlineData("8 fl oz milk", "milk")]
    public void Abbreviated_units_are_stripped(string raw, string expected) =>
        Assert.Equal(expected, _normalizer.Normalize(raw));

    /// <summary>
    /// The counterpart: a unit abbreviation is only a unit at the front, so one
    /// appearing inside a name survives. Leading units are stripped repeatedly
    /// rather than once — "1 can gallon pickles" loses both "can" and "gallon" —
    /// which is why the guard has to be about position, not about the word.
    /// </summary>
    [Theory]
    [InlineData("vitamin c tablets", "vitamin c tablet")]
    public void A_unit_word_inside_the_name_is_kept(string raw, string expected) =>
        Assert.Equal(expected, _normalizer.Normalize(raw));

    /// <summary>
    /// The cost of treating "t" as teaspoon, pinned rather than papered over.
    /// Leading units are stripped repeatedly, so "t" at the front of a name is
    /// taken as a measurement — "t bone steak" loses its "t". That is the
    /// accepted price of normalizing "t. salt", which is far more common, and
    /// "T-bone" hyphenated (the usual spelling) is unaffected.
    ///
    /// If this ever stops being an acceptable trade, this test is the thing that
    /// should change first.
    /// </summary>
    [Fact]
    public void Known_limitation_a_leading_t_is_read_as_teaspoon()
    {
        Assert.Equal("bone steak", _normalizer.Normalize("2 lb t bone steak"));
        Assert.Equal("t-bone steak", _normalizer.Normalize("2 lb t-bone steak"));
    }
}
