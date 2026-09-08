using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;

namespace RecipeApi.Infrastructure.Scraping;

public class RecipeUrlImporter(
    HttpClient http,
    IOptions<ScrapingOptions> options,
    ILogger<RecipeUrlImporter> logger) : IRecipeUrlImporter
{
    private readonly ScrapingOptions _options = options.Value;
    private readonly JsonLdRecipeExtractor _jsonLd = new();
    private readonly HtmlFallbackParser _fallback = new();

    public async Task<ImportedRecipeResult> ImportFromUrlAsync(string url, CancellationToken ct)
    {
        if (!FetchableUrl.TryCreate(url, out var uri, out var urlError))
            return ImportedRecipeResult.Fail(ImportFailure.InvalidUrl, urlError!);

        string html;
        try
        {
            var fetched = await FetchAsync(uri!, ct);
            if (!fetched.Success)
                return ImportedRecipeResult.Fail(fetched.Failure, fetched.Error!);

            html = fetched.Html!;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ImportedRecipeResult.Fail(ImportFailure.FetchFailed, "The site took too long to respond.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogInformation(ex, "Fetch failed for {Url}", uri);
            return ImportedRecipeResult.Fail(ImportFailure.FetchFailed, "Could not reach that URL.");
        }

        var extracted = _jsonLd.Extract(html) ?? _fallback.Extract(html);

        if (extracted is null || !extracted.IsUsable)
        {
            return ImportedRecipeResult.Fail(
                ImportFailure.NoRecipeFound,
                "No recipe data was found on that page.");
        }

        return ImportedRecipeResult.Ok(ToCreateRequest(extracted));
    }

    private record FetchOutcome(bool Success, string? Html, ImportFailure Failure, string? Error);

    /// <summary>
    /// Follows redirects by hand. Automatic redirects would re-point the request
    /// after the host check has already passed, so a public URL could bounce
    /// straight to an internal one; every hop is re-validated here instead.
    /// </summary>
    private async Task<FetchOutcome> FetchAsync(Uri uri, CancellationToken ct)
    {
        var current = uri;

        for (var hop = 0; hop <= _options.MaxRedirects; hop++)
        {
            if (!await FetchableUrl.IsHostAllowedAsync(current, _options.AllowLoopbackHosts, ct))
            {
                return new FetchOutcome(false, null, ImportFailure.BlockedHost,
                    "That host is not reachable for import.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                current = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(current, response.Headers.Location);

                if (!FetchableUrl.TryCreate(current.ToString(), out _, out var redirectError))
                    return new FetchOutcome(false, null, ImportFailure.BlockedHost, redirectError!);

                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return new FetchOutcome(false, null, ImportFailure.FetchFailed,
                    $"The site returned {(int)response.StatusCode}.");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null &&
                !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                return new FetchOutcome(false, null, ImportFailure.NoRecipeFound,
                    $"That URL returned {mediaType}, not a web page.");
            }

            var html = await ReadBoundedAsync(response, ct);

            return html is null
                ? new FetchOutcome(false, null, ImportFailure.FetchFailed, "That page is too large to import.")
                : new FetchOutcome(true, html, ImportFailure.None, null);
        }

        return new FetchOutcome(false, null, ImportFailure.FetchFailed, "Too many redirects.");
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
               or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Reads with a hard byte ceiling. Content-Length is a hint a server can lie
    /// about, so the running total is checked as well.
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

    private static CreateRecipeRequest ToCreateRequest(SchemaOrgRecipe source)
    {
        var prep = source.PrepTimeMinutes ?? 0;
        var cook = source.CookTimeMinutes ?? 0;

        // Many sites publish only totalTime. Attribute the remainder to cooking
        // rather than dropping it, so search on total time stays meaningful.
        if (source.TotalTimeMinutes is int total && total > 0)
        {
            if (prep == 0 && cook == 0)
                cook = total;
            else if (cook == 0 && total > prep)
                cook = total - prep;
        }

        var ingredients = source.Ingredients
            .Take(100)
            .Select(line => new CreateIngredientRequest(
                Truncate(line, 200),
                Quantity: null,   // parsed out of RawText by the normalizer; not modelled yet
                Unit: null,
                RawText: Truncate(line, 500)))
            .ToList();

        return new CreateRecipeRequest(
            Title: Truncate(source.Name!, 300),
            Description: source.Description is null ? null : Truncate(source.Description, 4000),
            PrepTimeMinutes: Math.Clamp(prep, 0, 10080),
            CookTimeMinutes: Math.Clamp(cook, 0, 10080),
            Servings: Math.Clamp(source.Servings ?? 4, 1, 1000),
            // schema.org has no difficulty field, and guessing one from step count
            // would be inventing data. Medium is the honest default.
            Difficulty: DifficultyLevel.Medium,
            ImageUrl: source.ImageUrl is null ? null : Truncate(source.ImageUrl, 2048),
            Cuisines: source.Cuisines.Take(5).Select(c => Truncate(c, 100)).ToList(),
            Tags: source.Keywords.Take(12).Select(k => Truncate(k, 100)).ToList(),
            Ingredients: ingredients,
            Steps: source.Steps.Take(100).Select(s => Truncate(s, 4000)).ToList());
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
