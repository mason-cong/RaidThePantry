using RecipeApi.Application.Services;

namespace RecipeApi.IntegrationTests.Unit;

/// <summary>
/// Every dropped case below is a keyword delish.com actually publishes. They
/// were all being stored and rendered to visitors as badges — 162 of 322 rows
/// in the tag table — including a raw content id out of the publisher's CMS.
/// </summary>
public class TagNameTests
{
    [Theory]
    [InlineData("contentId: d05c0f11-b8f4-414d-812b-f941ddacdd04")]
    [InlineData("content-type: Recipe")]
    [InlineData("displayType: recipe")]
    [InlineData("isSyndicated: false")]
    [InlineData("locale: US")]
    [InlineData("shortTitle: PSL Tiramisu")]
    [InlineData("sponsored: false")]
    [InlineData("subsection: Recipes")]
    public void Cms_metadata_is_dropped(string raw) =>
        Assert.Null(TagName.Normalize(raw));

    /// <summary>
    /// The exception. "collection: Desserts" is a real tag wearing a
    /// meaningless prefix, so the value survives and the prefix does not.
    /// </summary>
    [Theory]
    [InlineData("collection: Desserts", "Desserts")]
    [InlineData("collection: Fall Recipes", "Fall Recipes")]
    [InlineData("Collection: Latest & Greatest Recipes", "Latest & Greatest Recipes")]
    public void A_collection_prefix_is_stripped_and_its_value_kept(string raw, string expected) =>
        Assert.Equal(expected, TagName.Normalize(raw));

    [Theory]
    [InlineData("nut-free", "nut-free")]
    [InlineData("weeknight", "weeknight")]
    [InlineData("  one-pot   dinner ", "one-pot dinner")]
    public void Ordinary_tags_pass_through(string raw, string expected) =>
        Assert.Equal(expected, TagName.Normalize(raw));

    /// <summary>
    /// A colon does not automatically mean metadata. The key has to look like an
    /// identifier, so a bare time is not mistaken for a prefix.
    /// </summary>
    [Fact]
    public void A_colon_alone_does_not_make_it_metadata() =>
        Assert.Equal("5:00", TagName.Normalize("5:00"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_is_dropped(string? raw) =>
        Assert.Null(TagName.Normalize(raw));

    /// <summary>A label long enough to be a sentence is a description, not a tag.</summary>
    [Fact]
    public void An_overlong_label_is_dropped() =>
        Assert.Null(TagName.Normalize(new string('a', 61)));

    [Theory]
    [InlineData("Desserts")]
    [InlineData("nut-free")]
    public void Is_idempotent(string canonical) =>
        Assert.Equal(canonical, TagName.Normalize(canonical));
}
