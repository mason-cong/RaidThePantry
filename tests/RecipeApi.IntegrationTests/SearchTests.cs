using RecipeApi.Application.Dtos;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests;

/// <summary>
/// Guards the four defects the search path has already had. Every one of them
/// compiles cleanly and only fails against a real database, which is why these
/// are integration tests rather than unit tests over a fake.
/// </summary>
public class SearchTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private Task<PagedResult<RecipeSummaryDto>> SearchAsync(string query, string? token = null) =>
        Client.GetJsonAsync<PagedResult<RecipeSummaryDto>>($"/api/recipes?{query}", token);

    // ---------------------------------------------------------------- fix 1
    // Multi-keyword ingredient search was written as
    //     keywords.All(kw => r.Ingredients.Any(...))
    // which EF cannot translate — `.All()` over a client-side list against a
    // server-side navigation throws at runtime. It is now one Where per keyword.

    [Fact]
    public async Task Ingredient_keywords_combine_with_AND_not_OR()
    {
        var chicken = await SearchAsync("ingredients=chicken&pageSize=50");
        var chickenAndRice = await SearchAsync("ingredients=chicken&ingredients=rice&pageSize=50");

        Assert.Equal(3, chicken.TotalCount);
        Assert.Equal(2, chickenAndRice.TotalCount);

        // Adding a keyword can only narrow the result, never widen it.
        var wide = chicken.Items.Select(r => r.Id).ToHashSet();
        Assert.All(chickenAndRice.Items, r => Assert.Contains(r.Id, wide));

        // Tikka Masala has chicken but no rice, so AND must exclude it.
        Assert.DoesNotContain(chickenAndRice.Items, r => r.Title == "Chicken Tikka Masala");
    }

    [Fact]
    public async Task Comma_separated_keywords_behave_like_repeated_ones()
    {
        var repeated = await SearchAsync("ingredients=chicken&ingredients=rice&pageSize=50");
        var commaJoined = await SearchAsync("ingredients=chicken,rice&pageSize=50");

        Assert.Equal(repeated.TotalCount, commaJoined.TotalCount);
    }

    [Fact]
    public async Task Exact_ingredient_match_is_narrower_than_fuzzy()
    {
        var fuzzy = await SearchAsync("ingredients=garl&pageSize=50");
        var exact = await SearchAsync("ingredients=garl&exactIngredientMatch=true&pageSize=50");
        var exactFull = await SearchAsync("ingredients=garlic&exactIngredientMatch=true&pageSize=50");

        // "garl" matches garlic and garlic chives as a substring...
        Assert.Equal(8, fuzzy.TotalCount);
        // ...but is nobody's whole canonical name.
        Assert.Equal(0, exact.TotalCount);
        Assert.Equal(7, exactFull.TotalCount);
    }

    // ---------------------------------------------------------------- fix 2
    // User input reaches an ILIKE pattern, so LIKE metacharacters have to be
    // escaped. Unescaped, "%" matches every row.

    [Fact]
    public async Task Percent_is_matched_literally_not_as_a_wildcard()
    {
        var result = await SearchAsync("ingredients=%25&pageSize=50");

        // Exactly the two seeded recipes whose ingredients contain a literal "%"
        // ("2% milk", "70% dark chocolate") — not all 18.
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, r => r.Title == "Creamy Tomato Soup");
        Assert.Contains(result.Items, r => r.Title == "Dark Chocolate Mousse");
    }

    [Fact]
    public async Task Underscore_is_matched_literally_not_as_a_wildcard()
    {
        var result = await SearchAsync("ingredients=_&pageSize=50");

        // Unescaped this is "%_%", which matches any name of one character or
        // more — every recipe. No seeded ingredient contains a literal underscore.
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Hyphens_are_ordinary_characters()
    {
        var result = await SearchAsync("ingredients=sun-dried&pageSize=50");

        Assert.Equal("Sun-Dried Tomato Pasta", Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task Title_search_also_escapes_metacharacters()
    {
        var result = await SearchAsync("search=%25&pageSize=50");

        // No seeded title contains a percent sign.
        Assert.Equal(0, result.TotalCount);
    }

    // ---------------------------------------------------------------- fix 3
    // Every sort needs an Id tiebreaker. Without one, rows tying on the sort key
    // have no defined relative order, so a page boundary inside a tie can repeat
    // or drop a row. The seed contains a 4-way CreatedAt tie and 30/35-minute
    // groups of three specifically to force this.

    [Theory]
    [InlineData(null)]
    [InlineData("time")]
    [InlineData("title")]
    public async Task Paging_through_ties_yields_every_row_exactly_once(string? sortBy)
    {
        var sortParam = sortBy is null ? string.Empty : $"&sortBy={sortBy}";
        var seen = new List<Guid>();

        for (var page = 1; ; page++)
        {
            var result = await SearchAsync($"page={page}&pageSize=2{sortParam}");
            if (result.Items.Count == 0)
                break;

            seen.AddRange(result.Items.Select(r => r.Id));

            if (page >= result.TotalPages)
                break;
        }

        Assert.Equal(18, seen.Count);
        Assert.Equal(18, seen.Distinct().Count());
    }

    [Fact]
    public async Task Sort_by_time_is_ordered_and_stable()
    {
        var result = await SearchAsync("sortBy=time&pageSize=50");
        var times = result.Items.Select(r => r.TotalTimeMinutes).ToList();

        Assert.Equal(times.OrderBy(t => t), times);
    }

    // ---------------------------------------------------------------- fix 4
    // Page was never clamped, so page=0 produced a negative Skip and threw.

    [Theory]
    [InlineData("page=0", 1, 20)]
    [InlineData("page=-5", 1, 20)]
    [InlineData("pageSize=0", 1, 1)]
    [InlineData("pageSize=9999", 1, 100)]
    [InlineData("page=-1&pageSize=-1", 1, 1)]
    public async Task Paging_parameters_are_clamped_rather_than_throwing(
        string query, int expectedPage, int expectedPageSize)
    {
        var result = await SearchAsync(query);

        Assert.Equal(expectedPage, result.Page);
        Assert.Equal(expectedPageSize, result.PageSize);
    }

    // ------------------------------------------------------------- filtering

    [Theory]
    [InlineData("cuisines=Italian", 4)]
    [InlineData("cuisines=Italian,Japanese", 6)]     // cuisines are OR
    [InlineData("tags=vegan", 1)]
    [InlineData("tags=quick&tags=vegetarian", 13)]   // tags are OR too
    [InlineData("difficulty=0", 9)]
    [InlineData("maxTotalTimeMinutes=20", 3)]
    [InlineData("search=chicken", 3)]
    public async Task Filters_return_the_expected_slice(string query, int expected)
    {
        var result = await SearchAsync($"{query}&pageSize=50");

        Assert.Equal(expected, result.TotalCount);
    }

    [Fact]
    public async Task Filters_intersect_with_one_another()
    {
        var italian = await SearchAsync("cuisines=Italian&pageSize=50");
        var italianQuick = await SearchAsync("cuisines=Italian&tags=quick&pageSize=50");

        Assert.True(italianQuick.TotalCount < italian.TotalCount);
        Assert.All(italianQuick.Items, r => Assert.Contains("Italian", r.Cuisines));
    }

    [Fact]
    public async Task Max_total_time_is_applied_to_prep_plus_cook()
    {
        var result = await SearchAsync("maxTotalTimeMinutes=20&pageSize=50");

        Assert.All(result.Items, r => Assert.True(r.TotalTimeMinutes <= 20));
    }

    [Fact]
    public async Task Summary_carries_the_fields_a_list_view_needs()
    {
        var result = await SearchAsync("search=Margherita");
        var pizza = Assert.Single(result.Items);

        Assert.Equal(102, pizza.TotalTimeMinutes);       // 90 prep + 12 cook
        Assert.Contains("Italian", pizza.Cuisines);
    }

    // A recipe may legitimately list the same ingredient twice. The original
    // composite (RecipeId, IngredientId) key made that unrepresentable.
    [Fact]
    public async Task A_recipe_can_list_the_same_ingredient_twice()
    {
        var page = await SearchAsync("search=Margherita");
        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{page.Items.Single().Id}");

        var flour = recipe.Ingredients.Where(i => i.Name == "00 flour").ToList();

        Assert.Equal(2, flour.Count);
        Assert.Contains(flour, i => i.RawText!.Contains("dough"));
        Assert.Contains(flour, i => i.RawText!.Contains("dusting"));
    }

    [Fact]
    public async Task Detail_preserves_ingredient_and_step_order()
    {
        var page = await SearchAsync("search=Margherita");
        var recipe = await Client.GetJsonAsync<RecipeDetailDto>($"/api/recipes/{page.Items.Single().Id}");

        Assert.Equal("00 flour", recipe.Ingredients[0].Name);
        Assert.StartsWith("Mix flour", recipe.Steps[0]);
        Assert.Equal(5, recipe.Steps.Count);
    }
}
