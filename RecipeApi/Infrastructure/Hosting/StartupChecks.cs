using Microsoft.Extensions.Options;
using RecipeApi.Infrastructure.Scraping;

namespace RecipeApi.Infrastructure.Hosting;

/// <summary>
/// Refuses to start rather than starting wrong.
///
/// Every check here guards a setting whose misconfiguration is silent: the app
/// comes up, serves traffic, and is quietly insecure or quietly broken. A
/// stack trace at boot is a much cheaper way to find out.
/// </summary>
public static class StartupChecks
{
    public static void ValidateConfiguration(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            return;

        var problems = new List<string>();

        var scraping = app.Services.GetRequiredService<IOptions<ScrapingOptions>>().Value;

        // The one that matters most. This flag exists so the importer can be
        // pointed at a fixture server on 127.0.0.1; left on outside development
        // it re-opens POST /api/import/url as an SSRF tool against everything
        // else on the host, which is precisely what the guard exists to prevent.
        if (scraping.AllowLoopbackHosts)
        {
            problems.Add(
                "Scraping:AllowLoopbackHosts is true. That is a development-only setting — " +
                "it lets the import endpoint reach services on the host itself.");
        }

        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Postgres")))
        {
            problems.Add(
                "ConnectionStrings:Postgres is not set. Supply it as an environment variable: " +
                "ConnectionStrings__Postgres=\"Host=...;Database=...;Username=...;Password=...\"");
        }

        // Left at the template default the crawler is anonymous, which is rude at
        // best and gets the address blocked at worst.
        //
        // Compared against the default exactly, not searched for "example.com".
        // The substring version rejected every contact URL containing that
        // string — including recipes.example.com, which is what the README tells
        // you to use, so following the documentation produced an app that
        // refused to boot.
        if (string.IsNullOrWhiteSpace(scraping.UserAgent) ||
            scraping.UserAgent == ScrapingOptions.DefaultUserAgent)
        {
            problems.Add(
                "Scraping:UserAgent is still the built-in default. Set a real contact URL so a " +
                "site owner can reach you before they block you.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to start in {app.Environment.EnvironmentName}:{Environment.NewLine}  - " +
                string.Join($"{Environment.NewLine}  - ", problems));
        }
    }
}
