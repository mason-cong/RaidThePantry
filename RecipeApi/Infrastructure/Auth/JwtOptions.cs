using Microsoft.IdentityModel.Tokens;

namespace RecipeApi.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "https://localhost:7100";
    public string Audience { get; set; } = "recipeapi";
    public int ExpiryMinutes { get; set; } = 120;

    /// <summary>
    /// Base64 or raw text, at least 32 bytes for HS256. Never stored in an
    /// appsettings file — set it with:
    ///   dotnet user-secrets set "Jwt:SigningKey" "&lt;value&gt;" --project RecipeApi
    /// </summary>
    public string? SigningKey { get; set; }
}

/// <summary>
/// Wraps the resolved key so both the token issuer and the bearer validator
/// share one instance — if they drift apart, tokens this API issues fail its
/// own validation, which is a genuinely confusing bug to chase.
/// </summary>
public sealed class JwtSigningKey(SymmetricSecurityKey key)
{
    public SymmetricSecurityKey Key { get; } = key;
}
