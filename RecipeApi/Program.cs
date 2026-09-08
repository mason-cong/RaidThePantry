using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using RecipeApi.Application.Interfaces;
using RecipeApi.Application.Services;
using RecipeApi.Infrastructure.Auth;
using RecipeApi.Infrastructure.Persistence;
using RecipeApi.Infrastructure.Repositories;
using RecipeApi.Infrastructure.Scraping;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RecipeDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<IRecipeRepository, RecipeRepository>();
builder.Services.AddScoped<ILookupRepository, LookupRepository>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IIngredientNormalizer, IngredientNormalizer>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<RecipeImportService>();

builder.Services.Configure<ScrapingOptions>(builder.Configuration.GetSection(ScrapingOptions.SectionName));
builder.Services.AddHttpClient<PageFetcher>((sp, client) =>
    {
        var scraping = sp.GetRequiredService<IOptions<ScrapingOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(scraping.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(scraping.UserAgent);
    })
    // Redirects are followed by hand in PageFetcher so every hop is re-checked
    // against the SSRF guard; automatic redirects would bypass it.
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddScoped<IRecipeUrlImporter, RecipeUrlImporter>();

// Identity + JWT. Anonymous callers are unaffected: authentication only fills in
// claims when a token is present, and authorization only bites on [Authorize].
builder.Services.AddRecipeAuth(builder.Configuration, builder.Environment);

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
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

// Fallback rather than `Configuration["..."]!` — a missing key should not take
// the whole app down at startup.
var frontendOrigin = builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173";
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.WithOrigins(frontendOrigin).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// `dotnet run --project RecipeApi -- --seed [--reset]` seeds and exits without
// starting the web host.
if (args.Contains("--seed"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();
    await DbSeeder.SeedAsync(db, reset: args.Contains("--reset"));
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", async (RecipeDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok", database = "connected" })
        : Results.StatusCode(503));

app.Run();
