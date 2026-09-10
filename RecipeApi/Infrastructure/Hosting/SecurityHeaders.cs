namespace RecipeApi.Infrastructure.Hosting;

public static class SecurityHeaders
{
    /// <summary>
    /// img-src has to allow arbitrary https origins: recipe images are hotlinked
    /// from whichever site a recipe was imported or scraped from, so an allowlist
    /// would be a list of the entire web. data: is there for the inline emoji
    /// favicon.
    ///
    /// style-src keeps 'unsafe-inline' because React writes inline style
    /// attributes — RecipeCard hides a broken image with element.style.display —
    /// and CSP2 governs those under style-src. Dropping it silently breaks that
    /// fallback rather than reporting anything.
    ///
    /// script-src does NOT get 'unsafe-inline'. Everything ships in a bundle, so
    /// there is nothing legitimate to allow, and this is the directive that
    /// actually matters for XSS — which in this app means the auth token in
    /// localStorage.
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'; " +
        "img-src 'self' data: https:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "script-src 'self'; " +
        "connect-src 'self'; " +
        "font-src 'self' data:";

    public static IApplicationBuilder UseRecipeSecurityHeaders(this IApplicationBuilder app, bool servingSpa) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // frame-ancestors supersedes this for modern browsers; it is kept for
            // the ones that never implemented CSP framing directives.
            headers["X-Frame-Options"] = "DENY";

            // Only meaningful when this process is also serving the pages. A bare
            // API sending a page CSP would be noise.
            if (servingSpa)
            {
                headers["Content-Security-Policy"] = ContentSecurityPolicy;

                // The SPA shell must never be cached, or a deploy leaves browsers
                // running the previous bundle against the new API.
                //
                // Keyed off the response content type rather than the request
                // path, because the shell goes out by three different routes:
                // UseDefaultFiles rewriting "/", UseStaticFiles for
                // "/index.html", and MapFallbackToFile for every client-side
                // deep link. Only the middle one passes through
                // StaticFileOptions, so a path rule there silently misses the
                // other two — including "/" itself.
                context.Response.OnStarting(() =>
                {
                    if (headers.ContentType.ToString()
                        .StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        headers.CacheControl = "no-cache";
                    }

                    return Task.CompletedTask;
                });
            }

            await next();
        });
}
