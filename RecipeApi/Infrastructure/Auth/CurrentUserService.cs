using System.Security.Claims;
using RecipeApi.Application.Interfaces;

namespace RecipeApi.Infrastructure.Auth;

/// <summary>
/// Reads the caller's id from the JWT subject claim. Correct as written even
/// before authentication is wired in build-order step 7 — with no authentication
/// middleware there are simply no claims, so every caller is a guest, which is
/// exactly the intended behaviour for the read endpoints.
/// </summary>
public class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public Guid? UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? accessor.HttpContext?.User.FindFirstValue("sub");

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => UserId is not null;
}
