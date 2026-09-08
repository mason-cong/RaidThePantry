using System.Net;
using System.Text.Json;
using RecipeApi.Application.Dtos;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests;

public class AuthTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private static JsonElement DecodePayload(string jwt)
    {
        var segment = jwt.Split('.')[1];
        var padded = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=')
                            .Replace('-', '+').Replace('_', '/');

        return JsonDocument.Parse(Convert.FromBase64String(padded)).RootElement;
    }

    [Fact]
    public async Task Registering_returns_a_usable_token()
    {
        var email = $"new-{Guid.CreateVersion7():N}@example.test";

        using var response = await Client.SendAsync(HttpMethod.Post, "/api/auth/register",
            new { email, password = Api.TestPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.ReadAsync<AuthResponse>();
        Assert.True(auth.ExpiresAt > DateTimeOffset.UtcNow);

        var claims = DecodePayload(auth.AccessToken);
        Assert.True(Guid.TryParse(claims.GetProperty("sub").GetString(), out _));
        Assert.Equal(email, claims.GetProperty("email").GetString());
        Assert.Equal("recipeapi", claims.GetProperty("aud").GetString());
    }

    [Fact]
    public async Task Me_is_gated_and_reports_the_signed_in_account()
    {
        using (var anonymous = await Client.GetAsync("/api/auth/me", token: null))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var token = await Client.RegisterAsync("me");
        var user = await Client.GetJsonAsync<CurrentUserDto>("/api/auth/me", token);

        Assert.Equal(DecodePayload(token).GetProperty("sub").GetString(), user.Id.ToString());
        Assert.NotEqual(default, user.CreatedAt);
    }

    [Fact]
    public async Task Logging_in_returns_a_token_for_the_same_account()
    {
        var email = $"login-{Guid.CreateVersion7():N}@example.test";
        await Client.SendAsync(HttpMethod.Post, "/api/auth/register", new { email, password = Api.TestPassword });

        using var response = await Client.SendAsync(HttpMethod.Post, "/api/auth/login",
            new { email, password = Api.TestPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.ReadAsync<AuthResponse>()).AccessToken;

        var user = await Client.GetJsonAsync<CurrentUserDto>("/api/auth/me", token);
        Assert.Equal(email, user.Email);
    }

    /// <summary>
    /// A different message for "no such account" than for "wrong password" turns
    /// login into an account-existence oracle. They must be indistinguishable.
    /// </summary>
    [Fact]
    public async Task Login_does_not_reveal_whether_an_account_exists()
    {
        var email = $"oracle-{Guid.CreateVersion7():N}@example.test";
        await Client.SendAsync(HttpMethod.Post, "/api/auth/register", new { email, password = Api.TestPassword });

        using var wrongPassword = await Client.SendAsync(HttpMethod.Post, "/api/auth/login",
            new { email, password = "definitely-not-it" });

        using var unknownAccount = await Client.SendAsync(HttpMethod.Post, "/api/auth/login",
            new { email = $"nobody-{Guid.CreateVersion7():N}@example.test", password = "definitely-not-it" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(),
                     await unknownAccount.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registering_the_same_email_twice_is_rejected()
    {
        var email = $"dupe-{Guid.CreateVersion7():N}@example.test";
        await Client.SendAsync(HttpMethod.Post, "/api/auth/register", new { email, password = Api.TestPassword });

        using var second = await Client.SendAsync(HttpMethod.Post, "/api/auth/register",
            new { email, password = Api.TestPassword });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("Duplicate", await second.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("short", "password below the 10-character minimum")]
    [InlineData("", "empty password")]
    public async Task Weak_passwords_are_rejected(string password, string _)
    {
        using var response = await Client.SendAsync(HttpMethod.Post, "/api/auth/register",
            new { email = $"weak-{Guid.CreateVersion7():N}@example.test", password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_email_is_rejected()
    {
        using var response = await Client.SendAsync(HttpMethod.Post, "/api/auth/register",
            new { email = "not-an-email", password = Api.TestPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The policy is length-only by design (NIST SP 800-63B). A passphrase with
    /// no uppercase, digit or symbol must be accepted, or the policy in
    /// AuthServiceCollectionExtensions has silently drifted back to composition
    /// rules.
    /// </summary>
    [Fact]
    public async Task A_long_all_lowercase_passphrase_is_accepted()
    {
        using var response = await Client.SendAsync(HttpMethod.Post, "/api/auth/register",
            new { email = $"phrase-{Guid.CreateVersion7():N}@example.test", password = "correcthorsebatterystaple" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("not.a.token")]
    [InlineData("")]
    public async Task Malformed_tokens_are_rejected(string token)
    {
        using var response = await Client.GetAsync("/api/auth/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_tampered_signature_is_rejected()
    {
        var token = await Client.RegisterAsync("tamper");

        using var response = await Client.GetAsync("/api/auth/me", token[..^6] + "AAAAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
