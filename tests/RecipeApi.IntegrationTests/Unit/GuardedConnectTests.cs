using System.Net.Http;
using RecipeApi.Infrastructure.Scraping;
using RecipeApi.IntegrationTests.Fixtures;

namespace RecipeApi.IntegrationTests.Unit;

/// <summary>
/// GuardedConnect is where the SSRF rule is actually enforced, so it is tested
/// on its own rather than only through the import endpoint.
///
/// The distinction matters: FetchableUrl's pre-flight check runs before the
/// request and could be bypassed, removed, or simply beaten by DNS rebinding.
/// These drive a real HttpClient with nothing but the connect callback in place,
/// which is the arrangement that has to hold on its own.
/// </summary>
public class GuardedConnectTests
{
    private static HttpClient ClientWithGuard(bool allowLoopback) =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = GuardedConnect.Handler(allowLoopback)
        })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

    [Theory]
    // Literal loopback, both families.
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://[::1]:9/")]
    // A real public hostname that resolves to 127.0.0.1. The address is only
    // known after resolution, which is the case a literal-only check misses.
    [InlineData("http://localtest.me/")]
    // Cloud instance metadata — the payload this guard exists for.
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    // RFC 1918 space.
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://192.168.1.1/")]
    public async Task Refuses_to_connect_to_addresses_that_are_not_public(string url)
    {
        using var client = ClientWithGuard(allowLoopback: false);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));

        // Distinguishes a refusal from an ordinary connection failure: a closed
        // port would also throw here, and a test that accepted either would pass
        // with the guard deleted.
        Assert.Contains("not permitted", Flatten(error), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The other half of the contract. A guard that rejected everything would
    /// satisfy the tests above and break the feature.
    /// </summary>
    [Fact]
    public async Task Connects_normally_when_the_address_is_allowed()
    {
        await using var site = await FixtureSite.StartAsync();

        using var client = ClientWithGuard(allowLoopback: true);
        using var response = await client.GetAsync(site.Url("/simple"));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("schema.org", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Loopback is permitted only because the development flag says so. The same
    /// address with the flag off is refused, which is what keeps the fixture
    /// server from being a hole in production.
    /// </summary>
    [Fact]
    public async Task The_same_loopback_address_is_refused_when_the_flag_is_off()
    {
        await using var site = await FixtureSite.StartAsync();

        using var client = ClientWithGuard(allowLoopback: false);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(site.Url("/simple")));

        Assert.Contains("not permitted", Flatten(error), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>HttpClient wraps the callback's exception, so the message is nested.</summary>
    private static string Flatten(Exception error)
    {
        var messages = new List<string>();

        for (Exception? current = error; current is not null; current = current.InnerException)
            messages.Add(current.Message);

        return string.Join(" | ", messages);
    }
}
