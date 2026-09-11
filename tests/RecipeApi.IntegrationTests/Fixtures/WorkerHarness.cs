extern alias worker;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApi.Application.Interfaces;
using RecipeApi.Infrastructure.Persistence;
using RecipeApi.Infrastructure.Scraping;
using worker::RecipeApi.Worker;

namespace RecipeApi.IntegrationTests.Fixtures;

/// <summary>
/// Builds ScrapeJob and PromoteJob against the test database.
///
/// The jobs are driven directly rather than through the Worker's command line:
/// the argument parsing is a thin switch, and what is worth pinning down is what
/// the jobs do to the database.
///
/// The HttpClients are built with <see cref="GuardedHttpHandler"/> — the same
/// call the Worker makes — so these tests exercise the real handler rather than
/// a convenient one. That matters here specifically, because the Worker spent a
/// while with an unguarded handler and nothing noticed.
/// </summary>
internal sealed class WorkerHarness : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly HttpClient _fetchClient;
    private readonly HttpClient _robotsClient;

    public RecipeDbContext Db { get; }
    public ScrapingOptions Options { get; }

    public WorkerHarness(RecipeApiFactory factory, Action<ScrapingOptions>? configure = null)
    {
        _scope = factory.Services.CreateScope();
        Db = _scope.ServiceProvider.GetRequiredService<RecipeDbContext>();

        Options = new ScrapingOptions
        {
            // The fixture site is on loopback, which is the whole reason this
            // flag exists.
            AllowLoopbackHosts = true,

            // Otherwise every test pays the politeness delay between pages.
            PolitenessDelaySeconds = 0,

            ParserVersion = 1,
            UserAgent = "RecipeFinderBot/test (+https://recipefinder.invalid/bot)",
            UserAgentToken = "RecipeFinderBot"
        };

        configure?.Invoke(Options);

        _fetchClient = new HttpClient(GuardedHttpHandler.Create(Options.AllowLoopbackHosts))
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        _robotsClient = new HttpClient(
            GuardedHttpHandler.Create(Options.AllowLoopbackHosts, followRedirects: true))
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private IOptions<ScrapingOptions> Wrapped => Microsoft.Extensions.Options.Options.Create(Options);

    public ScrapeJob Scraper() => new(
        Db,
        new PageFetcher(_fetchClient, Wrapped),
        new RobotsTxtChecker(_robotsClient, Wrapped, NullLogger<RobotsTxtChecker>.Instance),
        Wrapped,
        NullLogger<ScrapeJob>.Instance);

    public PromoteJob Promoter() => new(
        Db,
        _scope.ServiceProvider.GetRequiredService<IRecipeRepository>(),
        Wrapped,
        NullLogger<PromoteJob>.Instance);

    public void Dispose()
    {
        _fetchClient.Dispose();
        _robotsClient.Dispose();
        _scope.Dispose();
    }
}
