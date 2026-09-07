using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;
using RecipeApi.Infrastructure.Persistence;

namespace RecipeApi.Infrastructure.Auth;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers Identity, JWT bearer validation and token issuance.
    ///
    /// Wiring this changes nothing for anonymous callers: authentication only
    /// populates claims when a token is present, and authorization only bites on
    /// endpoints that carry [Authorize]. Guest browsing stays intact.
    /// </summary>
    public static IServiceCollection AddRecipeAuth(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(section);

        var options = section.Get<JwtOptions>() ?? new JwtOptions();
        var signingKey = new JwtSigningKey(ResolveSigningKey(options, environment));
        services.AddSingleton(signingKey);

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;

                // Length instead of composition classes, per NIST SP 800-63B.
                // Character-class rules mostly produce "Password1!" — predictable
                // to an attacker, annoying to everyone else — while a longer
                // passphrase like "correct-horse-battery" is stronger and easier
                // to remember. 10 is the floor; nothing else is mandated.
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireDigit = false;
            })
            .AddEntityFrameworkStores<RecipeDbContext>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                // Keep "sub" as "sub" instead of silently rewriting it to the long
                // ClaimTypes.NameIdentifier URI, so the claim a token carries is the
                // claim the code reads.
                o.MapInboundClaims = false;

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey.Key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub
                };
            });

        services.AddAuthorization();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        return services;
    }

    private static SymmetricSecurityKey ResolveSigningKey(JwtOptions options, IWebHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(options.SigningKey))
        {
            var bytes = TryDecodeBase64(options.SigningKey) ?? Encoding.UTF8.GetBytes(options.SigningKey);

            if (bytes.Length < 32)
            {
                throw new InvalidOperationException(
                    $"Jwt:SigningKey is only {bytes.Length} bytes; HS256 requires at least 32. " +
                    "Generate one with: dotnet user-secrets set \"Jwt:SigningKey\" " +
                    "\"$(openssl rand -base64 32)\" --project RecipeApi");
            }

            return new SymmetricSecurityKey(bytes);
        }

        if (environment.IsDevelopment())
        {
            // Ephemeral key so `--seed` and a bare `dotnet run` work on a fresh
            // clone with no configuration. Tokens do not survive a restart, which
            // is why this is never allowed outside Development.
            Console.WriteLine(
                "warning: Jwt:SigningKey is not configured; using a random ephemeral key. " +
                "Tokens will be invalidated on restart. Set one with: " +
                "dotnet user-secrets set \"Jwt:SigningKey\" \"<32+ bytes>\" --project RecipeApi");

            return new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        }

        throw new InvalidOperationException(
            "Jwt:SigningKey must be configured outside Development. " +
            "Supply it through user-secrets, an environment variable, or your secret store.");
    }

    private static byte[]? TryDecodeBase64(string value)
    {
        Span<byte> buffer = stackalloc byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out var written) && written >= 32
            ? Convert.FromBase64String(value)
            : null;
    }
}
