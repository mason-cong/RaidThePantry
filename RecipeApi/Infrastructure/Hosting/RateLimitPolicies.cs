using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace RecipeApi.Infrastructure.Hosting;

public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Per address, covering register and login together.
    ///
    /// Deliberately not tight. Per-IP limiting is the wrong tool against someone
    /// guessing one account's password — they can rotate addresses — and the
    /// right tool against credential stuffing, which is many accounts at a few
    /// tries each. Set low enough to matter there and it starts rejecting an
    /// office or a household sharing one NAT, which is a support ticket rather
    /// than an attack. 30/min is useless for cracking a 10-character minimum and
    /// invisible to real traffic.
    /// </summary>
    public int AuthPerMinute { get; set; } = 30;

    /// <summary>
    /// Per account. Each import spends an outbound fetch against a third party,
    /// so this is as much about not becoming someone else's problem as it is
    /// about protecting this server.
    /// </summary>
    public int ImportPerMinute { get; set; } = 10;
}

/// <summary>
/// Two endpoints are worth limiting, for different reasons.
///
/// Import makes this server fetch a URL the caller chose. Even with the SSRF
/// guard in place, an unlimited version is a way to make our address hammer
/// someone else's site.
///
/// Auth is the credential-stuffing surface, limited per IP because an attacker
/// working through a list of leaked passwords has no account of their own.
///
/// Reads are deliberately not limited. Browsing is the anonymous default path
/// through this app, and a limit there would be a self-inflicted outage the first
/// time a link got shared.
/// </summary>
public static class RateLimitPolicies
{
    public const string ImportPolicy = "import";
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddRecipeRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Bound as options and read per request rather than pulled out of
        // IConfiguration here.
        //
        // Reading eagerly at registration looks equivalent and is not: under
        // WebApplicationFactory the test host's configuration overrides are
        // applied while the host is being built, which is after this method has
        // already run. An eager read silently gets the production defaults, and
        // the only symptom is the suite throttling itself.
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(ImportPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionByUserThenIp(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Limits(context).ImportPerMinute,
                    Window = TimeSpan.FromMinutes(1)
                }));

            options.AddPolicy(AuthPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionByIp(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Limits(context).AuthPerMinute,
                    Window = TimeSpan.FromMinutes(1)
                }));

            options.OnRejected = async (context, ct) =>
            {
                // Without Retry-After a client has no way to know how long to back
                // off, and the usual response is to retry immediately in a loop.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                // The content type goes through WriteAsJsonAsync, not a preceding
                // assignment to Response.ContentType — that gets overwritten with
                // application/json on the way out. Everything else in this API
                // reports failures as problem+json via Problem(); a 429 that
                // quietly differs makes client-side error handling special-case
                // it for no reason.
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc9110#section-15.5.29",
                    title = "Too many requests.",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "Slow down and try again shortly."
                }, options: null, contentType: "application/problem+json", ct);
            };
        });
    }

    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    /// <summary>
    /// Falls back to the address so an unauthenticated caller is still bounded —
    /// partitioning on a null user id would put every anonymous request into one
    /// shared bucket, which is a denial-of-service switch rather than a limit.
    /// </summary>
    private static string PartitionByUserThenIp(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub")
        ?? PartitionByIp(context);

    private static string PartitionByIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
