using System.Net;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests;

/// <summary>
/// Boots its own host with a real (tiny) limit, because the shared fixture runs
/// with the limiter effectively disabled — every other test registers an account
/// from loopback, and production limits would throttle the suite rather than the
/// code.
///
/// In ApiCollection so it does not run in parallel with the classes sharing that
/// database: this class re-seeds on startup like the others, and doing that
/// underneath a running test would fail it for unrelated reasons.
/// </summary>
[Collection(ApiCollection.Name)]
public class RateLimiterTests : IAsyncLifetime
{
    private const int Limit = 3;

    private RecipeApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new RecipeApiFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPerMinute"] = Limit.ToString()
        });

        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Auth_endpoints_start_rejecting_once_the_limit_is_spent()
    {
        var statuses = new List<HttpStatusCode>();

        // One over the limit. The credentials are deliberately wrong: what is
        // being measured is that the request was counted, not that it succeeded.
        for (var attempt = 0; attempt < Limit + 1; attempt++)
        {
            using var response = await _client.SendAsync(HttpMethod.Post, "/api/auth/login",
                new { email = "nobody@example.test", password = "wrong-password-here" });

            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(Limit), status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task A_throttled_response_says_how_long_to_wait()
    {
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            using var spend = await _client.SendAsync(HttpMethod.Post, "/api/auth/login",
                new { email = "nobody@example.test", password = "wrong-password-here" });
        }

        using var throttled = await _client.SendAsync(HttpMethod.Post, "/api/auth/login",
            new { email = "nobody@example.test", password = "wrong-password-here" });

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);

        // Without this a client has nothing to back off against, and the usual
        // behaviour is to retry immediately in a loop.
        Assert.True(throttled.Headers.Contains("Retry-After"),
            "A 429 must carry Retry-After.");

        Assert.Equal("application/problem+json",
            throttled.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Reads are the anonymous default path through this app. Throttling them
    /// would be a self-inflicted outage the first time a link was shared, so the
    /// policy is deliberately not applied there.
    /// </summary>
    [Fact]
    public async Task Browsing_is_not_rate_limited()
    {
        for (var attempt = 0; attempt < Limit * 4; attempt++)
        {
            using var response = await _client.GetAsync("/api/recipes?pageSize=1", token: null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
