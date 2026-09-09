using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RecipeApi.Application.Dtos;
using RecipeApi.Domain;

namespace RecipeApi.IntegrationTests.Fixtures;

/// <summary>
/// Thin HTTP helpers. Requests and responses are bound to the application's own
/// DTO types rather than to anonymous shapes, so a change to the wire contract
/// breaks compilation here instead of silently passing.
/// </summary>
internal static class Api
{
    /// <summary>
    /// camelCase, case-insensitive, and string enums — matching the API's
    /// serializer. The converter is not optional: the API emits "Easy", and
    /// without it every response carrying a Difficulty fails to deserialize.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task<HttpResponseMessage> SendAsync(
        this HttpClient client, HttpMethod method, string url, object? body = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);

        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetAsync(this HttpClient client, string url, string? token) =>
        client.SendAsync(HttpMethod.Get, url, token: token);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(Json);
        Assert.NotNull(value);
        return value!;
    }

    /// <summary>GET expecting 200, returning the deserialized body.</summary>
    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url, string? token = null)
    {
        using var response = await client.GetAsync(url, token);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<T>();
    }

    /// <summary>Registers a fresh account and returns its bearer token.</summary>
    public static async Task<string> RegisterAsync(this HttpClient client, string tag = "user")
    {
        var email = $"{tag}-{Guid.CreateVersion7():N}@example.test";

        using var response = await client.SendAsync(HttpMethod.Post, "/api/auth/register",
            new { email, password = TestPassword });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadAsync<AuthResponse>()).AccessToken;
    }

    public const string TestPassword = "correct-horse-battery";

    /// <summary>A valid recipe payload; override any field via the optional parameters.</summary>
    public static CreateRecipeRequest NewRecipe(
        string? title = null,
        List<CreateIngredientRequest>? ingredients = null,
        List<string>? steps = null,
        List<string>? cuisines = null,
        List<string>? tags = null,
        int prep = 10,
        int cook = 20,
        int servings = 2,
        DifficultyLevel difficulty = DifficultyLevel.Easy) =>
        new(
            Title: title ?? $"Test Recipe {Guid.CreateVersion7():N}",
            Description: "Created by the integration tests.",
            PrepTimeMinutes: prep,
            CookTimeMinutes: cook,
            Servings: servings,
            Difficulty: difficulty,
            ImageUrl: null,
            Cuisines: cuisines ?? [],
            Tags: tags ?? [],
            Ingredients: ingredients ?? [new CreateIngredientRequest("1 tsp salt", 1, "tsp", null)],
            Steps: steps ?? ["Do the thing."]);
}
