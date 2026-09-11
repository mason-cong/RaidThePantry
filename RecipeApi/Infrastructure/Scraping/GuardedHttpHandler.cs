namespace RecipeApi.Infrastructure.Scraping;

/// <summary>
/// The one way to build an HttpClient handler that fetches a caller-supplied URL.
///
/// It exists because the alternative already failed once: when the connect guard
/// was added, only the API's PageFetcher was switched over. The Worker — which
/// fetches far more URLs than the API ever will — kept a plain HttpClientHandler
/// and silently had no protection at all, and neither did its robots.txt client.
/// Two call sites, one of them updated. Routing both through here makes that
/// particular mistake impossible rather than merely unlikely.
/// </summary>
public static class GuardedHttpHandler
{
    /// <param name="followRedirects">
    /// False for page fetching, where PageFetcher walks the redirect chain by
    /// hand so each hop gets a readable "that host is not reachable" error.
    ///
    /// True is still safe: <see cref="GuardedConnect"/> runs per connection, not
    /// per request, so every hop of an automatically-followed redirect is vetted
    /// whether or not anything above notices the redirect happened.
    /// </param>
    public static SocketsHttpHandler Create(bool allowLoopback, bool followRedirects = false) =>
        new()
        {
            AllowAutoRedirect = followRedirects,
            ConnectCallback = GuardedConnect.Handler(allowLoopback)
        };
}
