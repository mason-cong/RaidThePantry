using Microsoft.EntityFrameworkCore;
using RecipeApi.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RecipeDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// Auth middleware is wired in build-order step 7, alongside Identity and the
// JWT token service. Until then every endpoint is anonymous.

app.MapControllers();

app.MapGet("/health", async (RecipeDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok", database = "connected" })
        : Results.StatusCode(503));

app.Run();
