using RecipeApi.Infrastructure.Scraping;

namespace RecipeApi.IntegrationTests.Unit;

/// <summary>
/// The robots.txt rules, tested directly against the parser.
///
/// These are the rules that decide whether this crawler is a good citizen or the
/// kind that gets an IP range blocked, and every one of them is a silent
/// failure: parse a group wrong and the crawl keeps working, just fetching pages
/// it was asked not to.
/// </summary>
public class RobotsTxtTests
{
    private const string Token = "RecipeFinderBot";

    private static bool Allows(string robotsTxt, string path) =>
        RobotsTxtChecker.Parse(robotsTxt, Token).Allows(path);

    [Fact]
    public void No_rules_allows_everything()
    {
        Assert.True(Allows("", "/anything"));
    }

    [Fact]
    public void The_wildcard_group_applies_when_there_is_no_named_one()
    {
        const string robots = """
            User-agent: *
            Disallow: /private/
            """;

        Assert.False(Allows(robots, "/private/page"));
        Assert.True(Allows(robots, "/public/page"));
    }

    /// <summary>
    /// A group naming this crawler replaces the wildcard group outright — it does
    /// not merge with it. Here the wildcard forbids everything and the named
    /// group forbids nothing, so merging the two would block the whole site.
    /// </summary>
    [Fact]
    public void A_named_group_wins_over_the_wildcard_entirely()
    {
        const string robots = """
            User-agent: *
            Disallow: /

            User-agent: RecipeFinderBot
            Disallow: /admin/
            """;

        Assert.True(Allows(robots, "/recipes/carbonara"));
        Assert.False(Allows(robots, "/admin/panel"));
    }

    [Fact]
    public void Another_crawlers_group_is_ignored()
    {
        const string robots = """
            User-agent: SomeOtherBot
            Disallow: /

            User-agent: *
            Disallow: /private/
            """;

        // The rules that apply are the wildcard's, not SomeOtherBot's.
        Assert.True(Allows(robots, "/recipes"));
        Assert.False(Allows(robots, "/private/x"));
    }

    [Fact]
    public void Consecutive_user_agent_lines_share_one_group()
    {
        const string robots = """
            User-agent: SomeOtherBot
            User-agent: RecipeFinderBot
            Disallow: /shared-rule/
            """;

        Assert.False(Allows(robots, "/shared-rule/page"));
    }

    /// <summary>
    /// Longest match wins, which is what lets a site carve an exception out of a
    /// broad Disallow. Getting this backwards silently blocks a whole site.
    /// </summary>
    [Fact]
    public void The_longest_matching_rule_wins()
    {
        const string robots = """
            User-agent: *
            Disallow: /recipes/
            Allow: /recipes/public/
            """;

        Assert.False(Allows(robots, "/recipes/secret"));
        Assert.True(Allows(robots, "/recipes/public/carbonara"));
    }

    [Fact]
    public void Allow_beats_disallow_at_equal_length()
    {
        const string robots = """
            User-agent: *
            Disallow: /x/
            Allow: /x/
            """;

        Assert.True(Allows(robots, "/x/page"));
    }

    [Fact]
    public void An_empty_disallow_means_allow_everything()
    {
        const string robots = """
            User-agent: *
            Disallow: /
            Disallow:
            """;

        Assert.True(Allows(robots, "/anything"));
    }

    [Theory]
    [InlineData("/*.pdf", "/files/report.pdf", false)]
    [InlineData("/*.pdf", "/files/report.html", true)]
    [InlineData("/private*/", "/private-area/x", false)]
    public void Wildcards_match_within_a_path(string rule, string path, bool expected)
    {
        var robots = $"""
            User-agent: *
            Disallow: {rule}
            """;

        Assert.Equal(expected, Allows(robots, path));
    }

    /// <summary>"$" anchors the rule to the end, so it must not match a prefix.</summary>
    [Fact]
    public void A_dollar_anchors_the_rule_to_the_end_of_the_path()
    {
        const string robots = """
            User-agent: *
            Disallow: /page$
            """;

        Assert.False(Allows(robots, "/page"));
        Assert.True(Allows(robots, "/page/sub"));
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        const string robots = """
            # a comment
            User-agent: *      # trailing comment

            Disallow: /private/   # another
            """;

        Assert.False(Allows(robots, "/private/x"));
        Assert.True(Allows(robots, "/public"));
    }

    [Fact]
    public void Crawl_delay_is_read_from_the_matching_group()
    {
        const string robots = """
            User-agent: *
            Crawl-delay: 2

            User-agent: RecipeFinderBot
            Crawl-delay: 5
            """;

        Assert.Equal(TimeSpan.FromSeconds(5), RobotsTxtChecker.Parse(robots, Token).CrawlDelay);
    }

    /// <summary>
    /// A site asking for an hour between requests would stall a run indefinitely,
    /// so the value is clamped rather than trusted outright.
    /// </summary>
    [Fact]
    public void An_absurd_crawl_delay_is_clamped()
    {
        const string robots = """
            User-agent: *
            Crawl-delay: 3600
            """;

        Assert.Equal(TimeSpan.FromSeconds(60), RobotsTxtChecker.Parse(robots, Token).CrawlDelay);
    }

    [Fact]
    public void Deny_all_blocks_everything()
    {
        Assert.False(RobotsRules.DenyAll.Allows("/"));
        Assert.False(RobotsRules.DenyAll.Allows("/anything/at/all"));
    }
}
