using Microsoft.EntityFrameworkCore;
using RecipeApi.Domain;

namespace RecipeApi.Infrastructure.Persistence;

/// <summary>
/// Development seed data, run via `dotnet run --project RecipeApi -- --seed`.
///
/// Deliberately a runtime seeder rather than EF's HasData: HasData bakes rows
/// into migration history, so every edit to the sample recipes would generate a
/// migration. This data is expected to churn.
///
/// The recipes below are chosen to provoke the search bugs that only surface at
/// runtime — see the CATEGORY comments on each group. An empty database makes
/// SearchAsync look correct while proving nothing.
/// </summary>
public static class DbSeeder
{
    // Fixed base date so runs are reproducible. Several recipes deliberately
    // share an offset — see the tie-breaking note below.
    private static readonly DateTimeOffset Base = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private sealed record SeedIngredient(string Name, decimal? Qty, string? Unit, string Raw);

    private sealed record SeedRecipe(
        string Title,
        string Description,
        int Prep,
        int Cook,
        int Servings,
        DifficultyLevel Difficulty,
        string[] Cuisines,
        string[] Tags,
        SeedIngredient[] Ingredients,
        string[] Steps,
        int DayOffset,
        RecipeSourceType Source = RecipeSourceType.Manual,
        string? SourceUrl = null);

    public static async Task SeedAsync(RecipeDbContext db, bool reset, CancellationToken ct = default)
    {
        if (reset)
        {
            // Children first. Recipes cascades to its own children at the DB level,
            // but being explicit keeps this correct if a cascade is ever relaxed.
            await db.RecipeIngredients.ExecuteDeleteAsync(ct);
            await db.RecipeCuisines.ExecuteDeleteAsync(ct);
            await db.RecipeTags.ExecuteDeleteAsync(ct);
            await db.RecipeSteps.ExecuteDeleteAsync(ct);
            await db.UserFavorites.ExecuteDeleteAsync(ct);
            await db.Recipes.ExecuteDeleteAsync(ct);
            await db.Ingredients.ExecuteDeleteAsync(ct);
            await db.Cuisines.ExecuteDeleteAsync(ct);
            await db.Tags.ExecuteDeleteAsync(ct);
            Console.WriteLine("Cleared existing recipe data.");
        }
        else if (await db.Recipes.AnyAsync(ct))
        {
            Console.WriteLine("Database already contains recipes; nothing seeded. Use --seed --reset to replace.");
            return;
        }

        var ingredients = new Dictionary<string, Ingredient>(StringComparer.Ordinal);
        var cuisines = new Dictionary<string, Cuisine>(StringComparer.Ordinal);
        var tags = new Dictionary<string, Tag>(StringComparer.Ordinal);

        foreach (var seed in Recipes)
        {
            var recipe = new Recipe
            {
                Title = seed.Title,
                Description = seed.Description,
                PrepTimeMinutes = seed.Prep,
                CookTimeMinutes = seed.Cook,
                Servings = seed.Servings,
                Difficulty = seed.Difficulty,
                SourceType = seed.Source,
                SourceUrl = seed.SourceUrl,
                // Scraped recipes have no owner, which makes them read-only once
                // the ownership rule lands in build-order step 8.
                CreatedByUserId = null,
                CreatedAt = Base.AddDays(seed.DayOffset)
            };

            var order = 0;
            foreach (var si in seed.Ingredients)
            {
                recipe.Ingredients.Add(new RecipeIngredient
                {
                    Ingredient = GetOrCreate(ingredients, si.Name, n => new Ingredient { Name = n }),
                    RawText = si.Raw,
                    Quantity = si.Qty,
                    Unit = si.Unit,
                    Order = order++
                });
            }

            foreach (var name in seed.Cuisines)
                recipe.Cuisines.Add(new RecipeCuisine
                {
                    Cuisine = GetOrCreate(cuisines, name, n => new Cuisine { Name = n })
                });

            foreach (var name in seed.Tags)
                recipe.Tags.Add(new RecipeTag
                {
                    Tag = GetOrCreate(tags, name, n => new Tag { Name = n })
                });

            var stepNumber = 1;
            foreach (var instruction in seed.Steps)
                recipe.Steps.Add(new RecipeStep { Order = stepNumber++, Instruction = instruction });

            db.Recipes.Add(recipe);
        }

        await db.SaveChangesAsync(ct);

        Console.WriteLine($"Seeded {Recipes.Length} recipes, {ingredients.Count} ingredients, " +
                          $"{cuisines.Count} cuisines, {tags.Count} tags.");
    }

    private static T GetOrCreate<T>(Dictionary<string, T> cache, string key, Func<string, T> create)
    {
        if (!cache.TryGetValue(key, out var value))
        {
            value = create(key);
            cache[key] = value;
        }
        return value;
    }

    // Ingredient names are lowercase throughout: IIngredientNormalizer guarantees a
    // lowercase canonical form, and the unique index on Ingredients.Name relies on it.
    private static readonly SeedRecipe[] Recipes =
    [
        // CATEGORY: same ingredient twice in one recipe. Proves the surrogate key on
        // RecipeIngredient — under the original composite (RecipeId, IngredientId) PK
        // this recipe cannot be saved at all.
        new("Margherita Pizza",
            "Blistered thin crust with San Marzano tomatoes, fresh mozzarella and basil.",
            90, 12, 4, DifficultyLevel.Medium,
            ["Italian"], ["vegetarian", "classic"],
            [
                new("00 flour", 500, "g", "500g 00 flour, for the dough"),
                new("00 flour", 30, "g", "30g 00 flour, for dusting"),
                new("water", 325, "ml", "325ml lukewarm water"),
                new("active dry yeast", 3, "g", "3g active dry yeast"),
                new("salt", 10, "g", "10g fine sea salt"),
                new("san marzano tomato", 400, "g", "1 tin (400g) San Marzano tomatoes"),
                new("fresh mozzarella", 250, "g", "250g fresh mozzarella, torn"),
                new("basil", null, null, "a handful of fresh basil leaves"),
                new("olive oil", 2, "tbsp", "2 tbsp extra virgin olive oil")
            ],
            [
                "Mix flour, water, yeast and salt into a shaggy dough; rest 20 minutes.",
                "Knead until smooth, then prove 8 hours at room temperature.",
                "Divide into four balls and prove a further hour.",
                "Stretch each ball on a surface dusted with the reserved flour.",
                "Top with crushed tomatoes, mozzarella and basil; bake as hot as your oven goes, 8-12 minutes."
            ],
            DayOffset: 0),

        // CATEGORY: shared ingredients across recipes (chicken breast, jasmine rice,
        // garlic). Multi-keyword AND search is meaningless without overlap.
        new("Chicken Fried Rice",
            "Day-old rice, high heat, and not much else.",
            15, 15, 4, DifficultyLevel.Easy,
            ["Chinese"], ["quick", "one-pan"],
            [
                new("jasmine rice", 600, "g", "600g cooked jasmine rice, chilled overnight"),
                new("chicken breast", 300, "g", "300g chicken breast, diced"),
                new("egg", 3, null, "3 eggs, beaten"),
                new("spring onion", 4, null, "4 spring onions, sliced"),
                new("garlic", 3, "clove", "3 garlic cloves, minced"),
                new("soy sauce", 3, "tbsp", "3 tbsp light soy sauce"),
                new("sesame oil", 1, "tsp", "1 tsp toasted sesame oil"),
                new("frozen pea", 100, "g", "100g frozen peas")
            ],
            [
                "Heat a wok until smoking; sear the chicken and set aside.",
                "Scramble the eggs in the same wok and set aside.",
                "Fry garlic briefly, add rice and press flat to catch some colour.",
                "Return chicken and egg, add peas, soy and sesame oil; toss through spring onions."
            ],
            DayOffset: 0),

        new("Chicken Tikka Masala",
            "Yoghurt-marinated chicken in a spiced tomato cream sauce.",
            30, 40, 4, DifficultyLevel.Medium,
            ["Indian", "British"], ["comfort-food"],
            [
                new("chicken breast", 700, "g", "700g chicken breast, cubed"),
                new("greek yoghurt", 200, "g", "200g full-fat Greek yoghurt"),
                new("garam masala", 2, "tbsp", "2 tbsp garam masala"),
                new("garlic", 4, "clove", "4 garlic cloves"),
                new("ginger", 30, "g", "30g fresh ginger"),
                new("tinned tomato", 400, "g", "1 tin (400g) chopped tomatoes"),
                new("double cream", 150, "ml", "150ml double cream"),
                new("coriander", null, null, "fresh coriander, to serve")
            ],
            [
                "Marinate the chicken in yoghurt, garam masala, garlic and ginger for at least 2 hours.",
                "Grill or pan-sear until charred at the edges.",
                "Build the sauce with tomatoes and the remaining aromatics; simmer 20 minutes.",
                "Stir in cream, return the chicken, and finish with coriander."
            ],
            DayOffset: 0),

        // CATEGORY: multi-cuisine and multi-tag, exercising the Any/Contains filters
        // past the single-value case.
        new("Banh Mi",
            "Vietnamese-French sandwich: pate, pickled daikon and carrot, coriander, chilli.",
            25, 10, 2, DifficultyLevel.Medium,
            ["Vietnamese", "French"], ["sandwich", "quick", "street-food"],
            [
                new("baguette", 2, null, "2 short baguettes"),
                new("pork belly", 300, "g", "300g pork belly, thinly sliced"),
                new("daikon", 150, "g", "150g daikon, julienned"),
                new("carrot", 150, "g", "150g carrot, julienned"),
                new("rice vinegar", 100, "ml", "100ml rice vinegar"),
                new("coriander", null, null, "a large handful of coriander"),
                new("mayonnaise", 3, "tbsp", "3 tbsp mayonnaise"),
                new("bird's eye chilli", 2, null, "2 bird's eye chillies, sliced")
            ],
            [
                "Pickle the daikon and carrot in vinegar and sugar for 30 minutes.",
                "Sear the pork belly until crisp.",
                "Split and lightly toast the baguettes, spread with mayonnaise.",
                "Fill with pork, drained pickles, coriander and chilli."
            ],
            DayOffset: 0),

        new("Shakshuka",
            "Eggs poached in a cumin-spiked pepper and tomato sauce.",
            10, 25, 3, DifficultyLevel.Easy,
            ["Middle Eastern", "Israeli"], ["vegetarian", "brunch", "one-pan"],
            [
                new("egg", 5, null, "5 eggs"),
                new("red pepper", 2, null, "2 red peppers, sliced"),
                new("tinned tomato", 400, "g", "1 tin (400g) chopped tomatoes"),
                new("onion", 1, null, "1 large onion, sliced"),
                new("garlic", 3, "clove", "3 garlic cloves"),
                new("cumin", 1, "tsp", "1 tsp ground cumin"),
                new("smoked paprika", 1, "tsp", "1 tsp smoked paprika"),
                new("feta", 100, "g", "100g feta, crumbled")
            ],
            [
                "Soften onion and peppers in olive oil, 10 minutes.",
                "Add garlic and spices, then tomatoes; simmer until thick.",
                "Make wells and crack in the eggs; cover and cook until whites set.",
                "Scatter over feta and serve with bread."
            ],
            DayOffset: 1),

        // CATEGORY: ingredient names containing LIKE metacharacters. A search for
        // "70%" or "2%" must escape the wildcard or it matches everything.
        new("Dark Chocolate Mousse",
            "Four ingredients, no flour, no gelatine.",
            20, 0, 6, DifficultyLevel.Medium,
            ["French"], ["dessert", "gluten-free"],
            [
                new("70% dark chocolate", 200, "g", "200g 70% dark chocolate, chopped"),
                new("egg", 4, null, "4 eggs, separated"),
                new("caster sugar", 50, "g", "50g caster sugar"),
                new("double cream", 150, "ml", "150ml double cream"),
                new("salt", null, null, "a pinch of salt")
            ],
            [
                "Melt the chocolate gently and let it cool to just above body temperature.",
                "Whisk yolks into the chocolate.",
                "Whip whites with sugar to soft peaks; whip cream separately.",
                "Fold cream then whites into the chocolate in three additions; chill 4 hours."
            ],
            DayOffset: 2),

        new("Creamy Tomato Soup",
            "Roasted tomatoes blitzed smooth, loosened with milk rather than stock.",
            15, 45, 4, DifficultyLevel.Easy,
            ["American"], ["vegetarian", "comfort-food"],
            [
                new("plum tomato", 1, "kg", "1kg plum tomatoes, halved"),
                new("onion", 1, null, "1 onion, quartered"),
                new("garlic", 6, "clove", "6 garlic cloves, unpeeled"),
                new("2% milk", 300, "ml", "300ml 2% milk"),
                new("half-and-half", 100, "ml", "100ml half-and-half"),
                new("basil", null, null, "a handful of basil"),
                new("olive oil", 3, "tbsp", "3 tbsp olive oil")
            ],
            [
                "Roast tomatoes, onion and garlic with olive oil at 200C for 40 minutes.",
                "Squeeze the garlic from its skins and blend everything smooth.",
                "Loosen with milk and half-and-half; warm through without boiling.",
                "Finish with torn basil."
            ],
            DayOffset: 3),

        // CATEGORY: hyphenated ingredient names, another LIKE edge case.
        new("Sun-Dried Tomato Pasta",
            "Store-cupboard pasta that tastes like it took longer.",
            10, 15, 2, DifficultyLevel.Easy,
            ["Italian"], ["quick", "vegetarian", "pantry"],
            [
                new("linguine", 200, "g", "200g linguine"),
                new("sun-dried tomato", 100, "g", "100g sun-dried tomatoes in oil, sliced"),
                new("garlic", 2, "clove", "2 garlic cloves, sliced"),
                new("pine nut", 40, "g", "40g pine nuts, toasted"),
                new("parmesan", 50, "g", "50g parmesan, grated"),
                new("rocket", 60, "g", "60g rocket"),
                new("chilli flake", 1, "tsp", "1 tsp chilli flakes")
            ],
            [
                "Cook the linguine, reserving a mugful of water.",
                "Warm the tomatoes, garlic and chilli in some of their oil.",
                "Toss pasta through with a splash of the water to emulsify.",
                "Fold in rocket, pine nuts and parmesan off the heat."
            ],
            DayOffset: 4),

        // CATEGORY: sort-key ties. The next four all total 30 minutes, so
        // ORDER BY (prep + cook) without an Id tiebreaker can duplicate or drop
        // rows across page boundaries.
        new("Spaghetti Carbonara",
            "Guanciale, egg, pecorino, pepper. No cream.",
            10, 20, 2, DifficultyLevel.Medium,
            ["Italian"], ["quick", "classic"],
            [
                new("spaghetti", 200, "g", "200g spaghetti"),
                new("guanciale", 120, "g", "120g guanciale, cut into lardons"),
                new("egg", 3, null, "2 whole eggs plus 1 yolk"),
                new("pecorino romano", 60, "g", "60g pecorino romano, finely grated"),
                new("black pepper", null, null, "plenty of coarsely ground black pepper")
            ],
            [
                "Render the guanciale slowly until crisp; keep the fat.",
                "Beat eggs with pecorino and a lot of pepper.",
                "Drain the pasta, off the heat toss with the fat, then the egg mixture.",
                "Loosen with pasta water until glossy."
            ],
            DayOffset: 5),

        new("Beef Tacos",
            "Chipotle-braised beef, soft corn tortillas, raw onion and lime.",
            10, 20, 4, DifficultyLevel.Easy,
            ["Mexican"], ["quick", "street-food"],
            [
                new("beef mince", 500, "g", "500g beef mince"),
                new("corn tortilla", 8, null, "8 corn tortillas"),
                new("chipotle in adobo", 2, "tbsp", "2 tbsp chipotle in adobo"),
                new("onion", 1, null, "1 white onion, finely diced"),
                new("lime", 2, null, "2 limes, in wedges"),
                new("coriander", null, null, "coriander, chopped"),
                new("cumin", 2, "tsp", "2 tsp ground cumin")
            ],
            [
                "Brown the mince hard, breaking it up as little as possible.",
                "Add cumin and chipotle; cook down 10 minutes.",
                "Warm the tortillas directly over a flame.",
                "Serve with raw onion, coriander and lime."
            ],
            DayOffset: 5),

        new("Miso Soup",
            "Dashi, miso, tofu, wakame. Fifteen minutes start to finish.",
            5, 10, 2, DifficultyLevel.Easy,
            ["Japanese"], ["quick", "vegetarian", "light"],
            [
                new("dashi stock", 600, "ml", "600ml dashi stock"),
                new("white miso paste", 3, "tbsp", "3 tbsp white miso paste"),
                new("silken tofu", 200, "g", "200g silken tofu, cubed"),
                new("wakame", 5, "g", "5g dried wakame"),
                new("spring onion", 2, null, "2 spring onions, sliced")
            ],
            [
                "Rehydrate the wakame in cold water for 5 minutes.",
                "Warm the dashi; do not let it boil.",
                "Slake the miso with a ladle of stock, then stir it back in.",
                "Add tofu and wakame, warm through, top with spring onion."
            ],
            DayOffset: 6),

        new("Greek Salad",
            "No lettuce. Good tomatoes, good oil, good feta.",
            15, 0, 4, DifficultyLevel.Easy,
            ["Greek", "Mediterranean"], ["quick", "vegetarian", "gluten-free", "light"],
            [
                new("plum tomato", 500, "g", "500g ripe tomatoes, in wedges"),
                new("cucumber", 1, null, "1 cucumber, thickly sliced"),
                new("red onion", 1, null, "1 small red onion, thinly sliced"),
                new("feta", 200, "g", "200g feta, in a slab"),
                new("kalamata olive", 100, "g", "100g kalamata olives"),
                new("dried oregano", 1, "tsp", "1 tsp dried oregano"),
                new("olive oil", 4, "tbsp", "4 tbsp extra virgin olive oil")
            ],
            [
                "Salt the tomatoes and let them sit 10 minutes.",
                "Combine with cucumber, onion and olives.",
                "Lay the feta on top whole, dress with oil and oregano."
            ],
            DayOffset: 6),

        // CATEGORY: vegan/gluten-free tag filtering, plus more rice overlap.
        new("Chana Masala",
            "Chickpeas braised with tomato, ginger and amchur.",
            15, 35, 4, DifficultyLevel.Easy,
            ["Indian"], ["vegan", "vegetarian", "gluten-free", "budget"],
            [
                new("chickpea", 800, "g", "2 tins (800g) chickpeas, drained"),
                new("tinned tomato", 400, "g", "1 tin (400g) chopped tomatoes"),
                new("onion", 2, null, "2 onions, finely chopped"),
                new("ginger", 30, "g", "30g ginger, grated"),
                new("garlic", 4, "clove", "4 garlic cloves"),
                new("garam masala", 2, "tsp", "2 tsp garam masala"),
                new("amchur", 1, "tsp", "1 tsp amchur (dried mango powder)"),
                new("coriander", null, null, "coriander, to finish")
            ],
            [
                "Brown the onions properly — 15 minutes, not 5.",
                "Add ginger, garlic and spices; cook until fragrant.",
                "Add tomatoes and chickpeas with a splash of water; simmer 20 minutes.",
                "Finish with amchur and coriander."
            ],
            DayOffset: 7),

        new("Vegetable Biryani",
            "Layered spiced rice and vegetables, steamed under a tight lid.",
            35, 45, 6, DifficultyLevel.Hard,
            ["Indian"], ["vegetarian", "one-pan"],
            [
                new("basmati rice", 500, "g", "500g basmati rice, soaked 30 minutes"),
                new("cauliflower", 300, "g", "300g cauliflower florets"),
                new("carrot", 200, "g", "200g carrots, diced"),
                new("frozen pea", 150, "g", "150g frozen peas"),
                new("greek yoghurt", 200, "g", "200g yoghurt"),
                new("saffron", null, null, "a good pinch of saffron in warm milk"),
                new("garam masala", 2, "tbsp", "2 tbsp garam masala"),
                new("ghee", 60, "g", "60g ghee"),
                new("onion", 3, null, "3 onions, sliced and fried crisp")
            ],
            [
                "Parboil the rice to 70 percent and drain.",
                "Cook the vegetables with yoghurt and spices until nearly done.",
                "Layer rice over vegetables, scatter fried onions, drizzle saffron milk and ghee.",
                "Seal the pot and steam on the lowest heat for 25 minutes. Rest before opening."
            ],
            DayOffset: 8),

        new("Mushroom Risotto",
            "Carnaroli, dried porcini stock, finished hard with butter and parmesan.",
            20, 35, 4, DifficultyLevel.Medium,
            ["Italian"], ["vegetarian", "comfort-food"],
            [
                new("carnaroli rice", 320, "g", "320g carnaroli rice"),
                new("dried porcini", 30, "g", "30g dried porcini"),
                new("chestnut mushroom", 300, "g", "300g chestnut mushrooms, sliced"),
                new("white wine", 150, "ml", "150ml dry white wine"),
                new("parmesan", 80, "g", "80g parmesan, grated"),
                new("butter", 60, "g", "60g cold butter, cubed"),
                new("shallot", 2, null, "2 shallots, finely diced"),
                new("vegetable stock", 1.2m, "l", "1.2l hot vegetable stock")
            ],
            [
                "Soak the porcini in hot water; keep the liquor and add it to the stock.",
                "Sweat shallots, toast the rice, deglaze with wine.",
                "Add stock a ladle at a time for about 18 minutes.",
                "Off the heat, beat in cold butter and parmesan until it flows."
            ],
            DayOffset: 9),

        new("Pad Thai",
            "Tamarind, palm sugar and fish sauce in balance; everything else is garnish.",
            25, 10, 2, DifficultyLevel.Hard,
            ["Thai"], ["street-food"],
            [
                new("rice noodle", 200, "g", "200g flat rice noodles, soaked"),
                new("prawn", 200, "g", "200g raw prawns"),
                new("egg", 2, null, "2 eggs"),
                new("tamarind paste", 3, "tbsp", "3 tbsp tamarind paste"),
                new("fish sauce", 3, "tbsp", "3 tbsp fish sauce"),
                new("palm sugar", 3, "tbsp", "3 tbsp palm sugar"),
                new("beansprout", 150, "g", "150g beansprouts"),
                new("peanut", 50, "g", "50g roasted peanuts, crushed"),
                new("garlic chive", 50, "g", "50g garlic chives, in batons")
            ],
            [
                "Dissolve palm sugar with tamarind and fish sauce; taste for balance.",
                "Sear prawns, push aside, scramble the eggs.",
                "Add drained noodles and sauce, tossing until absorbed.",
                "Finish with beansprouts, chives and peanuts off the heat."
            ],
            DayOffset: 10),

        new("Chicken Katsu Curry",
            "Panko-crumbed chicken with a mild, faintly sweet curry sauce.",
            25, 30, 4, DifficultyLevel.Medium,
            ["Japanese"], ["comfort-food"],
            [
                new("chicken breast", 4, null, "4 chicken breasts, butterflied"),
                new("panko breadcrumb", 150, "g", "150g panko breadcrumbs"),
                new("egg", 2, null, "2 eggs, beaten"),
                new("plain flour", 80, "g", "80g plain flour"),
                new("jasmine rice", 300, "g", "300g jasmine rice"),
                new("carrot", 2, null, "2 carrots, diced"),
                new("onion", 1, null, "1 onion, diced"),
                new("curry powder", 2, "tbsp", "2 tbsp mild curry powder"),
                new("chicken stock", 600, "ml", "600ml chicken stock")
            ],
            [
                "Soften onion and carrot, add curry powder and flour to make a roux.",
                "Whisk in stock and simmer 20 minutes, then blend smooth.",
                "Flour, egg and panko the chicken; shallow fry until deep gold.",
                "Slice and serve over rice with the sauce."
            ],
            DayOffset: 11),

        // CATEGORY: scraped content. No owner, so it must be rejected by the
        // ownership check in step 8, and it carries a SourceUrl for the unique index.
        new("Slow Cooker Pulled Pork",
            "Pork shoulder cooked to collapse, then forked through its own juices.",
            20, 480, 8, DifficultyLevel.Easy,
            ["American"], ["slow-cooker", "budget", "gluten-free"],
            [
                new("pork shoulder", 2, "kg", "2kg boneless pork shoulder"),
                new("smoked paprika", 2, "tbsp", "2 tbsp smoked paprika"),
                new("brown sugar", 3, "tbsp", "3 tbsp dark brown sugar"),
                new("cider vinegar", 100, "ml", "100ml cider vinegar"),
                new("onion", 2, null, "2 onions, thickly sliced"),
                new("garlic", 6, "clove", "6 garlic cloves"),
                new("mustard powder", 1, "tbsp", "1 tbsp mustard powder")
            ],
            [
                "Rub the pork with paprika, sugar and mustard powder; rest overnight if you can.",
                "Lay onions in the slow cooker, sit the pork on top, add vinegar.",
                "Cook on low for 8 hours until it yields to a fork.",
                "Shred, then return to the reduced juices."
            ],
            DayOffset: 12,
            Source: RecipeSourceType.BulkScrape,
            SourceUrl: "https://example.com/recipes/slow-cooker-pulled-pork")
    ];
}
