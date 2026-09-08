using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;

namespace RecipeApi.Controllers;

/// <summary>
/// The only wholly auth-gated controller. Everything else stays readable by
/// guests; saving a recipe is one of the two things an account is actually for.
/// </summary>
[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoritesController(
    IFavoriteRepository favorites,
    ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>The signed-in user's saved recipes, most recently saved first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<RecipeSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<RecipeSummaryDto>>> GetFavorites(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (currentUser.UserId is not Guid userId)
            return Unauthorized();

        return Ok(await favorites.GetFavoritesAsync(userId, page, pageSize, ct));
    }

    /// <summary>
    /// Save a recipe. Idempotent: favoriting something already saved is 204, not
    /// a conflict — the caller asked for a state, and that state holds.
    /// </summary>
    [HttpPost("{recipeId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(Guid recipeId, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            return Unauthorized();

        return await favorites.AddAsync(userId, recipeId, ct) switch
        {
            FavoriteResult.RecipeNotFound => NotFound(),
            _ => NoContent()
        };
    }

    /// <summary>
    /// Unsave a recipe. Also idempotent — removing something that was not saved
    /// still leaves the caller where they asked to be, so it is 204 rather than
    /// 404. Any recipe can be unfavorited regardless of who owns it.
    /// </summary>
    [HttpDelete("{recipeId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Remove(Guid recipeId, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            return Unauthorized();

        await favorites.RemoveAsync(userId, recipeId, ct);
        return NoContent();
    }
}
