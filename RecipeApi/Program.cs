using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using RecipeApi.Application.Interfaces;
using RecipeApi.Application.Services;
using RecipeApi.Infrastructure.Auth;
using RecipeApi.Infrastructure.Hosting;
using RecipeApi.Infrastructure.Persistence;
using RecipeApi.Infrastructure.Repositories;
using RecipeApi.Infrastructure.Scraping;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RecipeDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<IRecipeRepository, RecipeRepository>();
builder.Services.AddScoped<ILookupRepository, LookupRepository>();
builder.Services.AddScoped<IFavoriteRepository, FavoriteRepository>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IIngredientNormalizer, IngredientNormalizer>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<RecipeImportService>();

builder.Services.Configure<ScrapingOptions>(builder.Configuration.GetSection(ScrapingOptions.SectionName));
// Same client configuration as the Worker's crawler, from one place. The import
// endpoint fetches the same sites, so it needs the same headers — without them a
// CDN answers 403 and the user is told the page could not be reached.
builder.Services.AddHttpClient<PageFetcher>((sp, client) =>
        ScrapingHttpDefaults.Apply(client, sp.GetRequiredService<IOptions<ScrapingOptions>>().Value))
    // Redirects are followed by hand in PageFetcher, and the connect callback is
    // where the SSRF rule is enforced.
    .ConfigurePrimaryHttpMessageHandler(sp =>
        ScrapingHttpDefaults.Handler(sp.GetRequiredService<IOptions<ScrapingOptions>>().Value));

builder.Services.AddScoped<IRecipeUrlImporter, RecipeUrlImporter>();

// Identity + JWT. Anonymous callers are unaffected: authentication only fills in
// claims when a token is present, and authorization only bites on [Authorize].
builder.Services.AddRecipeAuth(builder.Configuration, builder.Environment);

builder.Services.AddRecipeRateLimiting(builder.Configuration);

// Turns unhandled exceptions into ProblemDetails instead of an empty 500 body,
// without ever including the stack trace outside Development.
builder.Services.AddProblemDetails();

builder.Services.AddHttpContextAccessor();

builder.Services.AddControllers()
    // Enums on the wire as names ("Easy"), not ordinals (0). A client that reads
    // `"difficulty": 0` has to carry its own copy of the mapping, which silently
    // means something different the day a value is inserted into the enum.
    // Inbound binding already accepted both, so only responses change shape.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Token from /api/auth/login. Swagger adds the 'Bearer ' prefix for you."
    });

    o.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

// CORS is opt-in and has no default origin.
//
// In the shipped topology the API serves the SPA from wwwroot, so the browser
// only ever sees one origin and cross-origin rules never come into play — and in
// development the Vite proxy achieves the same thing. A policy configured "just
// in case" would be a standing grant nobody is exercising. Set
// Cors:AllowedOrigins only if the frontend is genuinely split onto its own host.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
        p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));
}

// Only honoured when Hosting:BehindReverseProxy is set, because trusting
// X-Forwarded-* from anyone lets a caller spoof their address and walk straight
// past the per-IP rate limits. Turn it on when something in front of this
// terminates TLS, and only then.
var behindProxy = builder.Configuration.GetValue("Hosting:BehindReverseProxy", false);
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // The proxy's address is not knowable in a container, so the platform is
        // trusted to overwrite these headers on the way in. That assumption is
        // the whole reason this is behind a flag.
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

var app = builder.Build();

// `--migrate` and `--seed` both run and exit without starting the web host, so
// they can be a deliberate deploy step rather than something racing on boot.
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();
    app.Logger.LogInformation("Applying migrations…");
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Migrations applied.");
    return;
}

if (args.Contains("--seed"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();
    await DbSeeder.SeedAsync(db, reset: args.Contains("--reset"));
    return;
}

app.ValidateConfiguration();

if (behindProxy)
    app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler();

    // TLS termination is the platform's job, so there is no UseHttpsRedirection
    // here — at the app layer, behind a proxy, it is the classic way to build a
    // redirect loop. HSTS is still worth sending, and its middleware already
    // skips loopback hosts.
    app.UseHsts();
}

// wwwroot only exists in a built image, where the Dockerfile copies the SPA into
// it. Running the API alone for local development finds nothing here and skips
// the whole static-file path, which is correct: Vite is serving the frontend.
var webRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var servingSpa = Directory.Exists(webRoot);

app.UseRecipeSecurityHeaders(servingSpa);

if (corsOrigins.Length > 0)
    app.UseCors();

app.UseRateLimiter();

if (servingSpa)
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            // Vite fingerprints everything under /assets — the filename changes
            // whenever the contents do — so those can be cached indefinitely.
            // Not caching the shell is handled in UseRecipeSecurityHeaders,
            // which sees every route the shell can be served by; this one only
            // sees the static-file route.
            var path = ctx.Context.Request.Path.Value ?? string.Empty;

            if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
                ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
        }
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", async (RecipeDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok", database = "connected" })
        : Results.StatusCode(503));

if (servingSpa)
{
    // Ordering here is by route specificity, not registration: the literal "api"
    // segment beats the catch-all, so an unmatched /api/... still answers as
    // JSON. Without this the SPA shell is returned with 200 for a mistyped
    // endpoint, and the client parses HTML as a recipe.
    app.MapFallback("api/{**rest}", () => Results.Problem(
        statusCode: StatusCodes.Status404NotFound, title: "No such endpoint."));

    // Deep links are routed by React Router, so any other unmatched path has to
    // return the shell rather than a 404.
    app.MapFallbackToFile("index.html");
}

app.Run();

/// <summary>
/// Top-level statements compile into an internal Program class, which
/// WebApplicationFactory&lt;Program&gt; cannot reach. Declaring it public here is
/// what lets the integration tests boot this exact application rather than a
/// reassembled copy of it.
/// </summary>
public partial class Program;
