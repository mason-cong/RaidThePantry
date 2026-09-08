using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using RecipeApi.Application.Interfaces;

namespace RecipeApi.Infrastructure.Scraping;

public record PageFetchResult(
    bool Success,
    string? Html,
    int? StatusCode,
    Uri? FinalUrl,
    ImportFailure Failure,
    string? Error);

/// <summary>
/// Fetching a caller-supplied page, safely. Shared by the API's synchronous
/// import and the Worker's bulk crawl — they differ in what they do with the
/// result, not in how it is obtained, and the SSRF rules must not diverge
/// between them.
/// </summary>
public class PageFetcher(HttpClient http, IOptions<ScrapingOptions> options)
{
    private readonly ScrapingOptions _options = options.Value;

    public async Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
    {
        var current = uri;

        for (var hop = 0; hop <= _options.MaxRedirects; hop++)
        {
            if (!await FetchableUrl.IsHostAllowedAsync(current, _options.AllowLoopbackHosts, ct))
                return Fail(ImportFailure.BlockedHost, "That host is not reachable for import.");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                current = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(current, response.Headers.Location);

                // Re-validated at the top of the next iteration; automatic
                // redirects would skip that check entirely.
                if (!FetchableUrl.TryCreate(current.ToString(), out _, out var redirectError))
                    return Fail(ImportFailure.BlockedHost, redirectError!);

                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return new PageFetchResult(false, null, (int)response.StatusCode, current,
                    ImportFailure.FetchFailed, $"The site returned {(int)response.StatusCode}.");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null &&
                !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                return new PageFetchResult(false, null, (int)response.StatusCode, current,
                    ImportFailure.NoRecipeFound, $"That URL returned {mediaType}, not a web page.");
            }

            var html = await ReadBoundedAsync(response, ct);

            return html is null
                ? new PageFetchResult(false, null, (int)response.StatusCode, current,
                    ImportFailure.FetchFailed, "That page is too large to import.")
                : new PageFetchResult(true, html, (int)response.StatusCode, current, ImportFailure.None, null);
        }

        return Fail(ImportFailure.FetchFailed, "Too many redirects.");
    }

    private static PageFetchResult Fail(ImportFailure failure, string error) =>
        new(false, null, null, null, failure, error);

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
               or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Content-Length is a hint a server can lie about, so the running total is
    /// checked as well.
    /// </summary>
    private async Task<string?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > _options.MaxResponseBytes)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();

        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > _options.MaxResponseBytes)
                return null;

            buffer.Write(chunk, 0, read);
        }

        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        var encoding = Encoding.UTF8;

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { encoding = Encoding.GetEncoding(charset); }
            catch (ArgumentException) { /* unknown charset: UTF-8 is the safer guess */ }
        }

        return encoding.GetString(buffer.ToArray());
    }
}
