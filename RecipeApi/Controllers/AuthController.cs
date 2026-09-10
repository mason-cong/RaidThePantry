using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;
using RecipeApi.Infrastructure.Hosting;

namespace RecipeApi.Controllers;

[ApiController]
[Route("api/auth")]
// Applies to register and login; GET /me opts back out below. Password guessing
// is the thing being throttled, and it is throttled per address because an
// attacker doing it has no account of their own.
[EnableRateLimiting(RateLimitPolicies.AuthPolicy)]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    IJwtTokenService tokenService,
    ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>Create an account. Returns a token, so registering also signs you in.</summary>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = request.Email,
            Email = request.Email,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code, error.Description);

            return ValidationProblem(ModelState);
        }

        return Ok(tokenService.CreateToken(user));
    }

    /// <summary>Exchange email and password for a bearer token.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        // One message for both "no such account" and "wrong password": splitting
        // them turns this endpoint into an account-existence oracle.
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new ProblemDetails { Title = "Invalid email or password.", Status = 401 });

        return Ok(tokenService.CreateToken(user));
    }

    /// <summary>The signed-in account. Lets a frontend check whether its token is still good.</summary>
    [HttpGet("me")]
    [Authorize]
    // The frontend calls this on every load to check whether a stored token is
    // still good. It is a cheap indexed read and it is not a guessing target, so
    // the per-IP auth limit would only punish several people sharing an address.
    [DisableRateLimiting]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> Me()
    {
        if (currentUser.UserId is not Guid userId)
            return Unauthorized();

        var user = await userManager.FindByIdAsync(userId.ToString());

        // Token validated but the account is gone — deleted since it was issued.
        return user is null
            ? Unauthorized()
            : Ok(new CurrentUserDto(user.Id, user.Email ?? string.Empty, user.CreatedAt));
    }
}
