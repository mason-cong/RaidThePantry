using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecipeApi.Application.Interfaces;
using RecipeApi.Application.Services;

namespace RecipeApi.Controllers;

public record ImportRecipeFromUrlRequest([Required, MaxLength(2048)] string Url);

[ApiController]
[Route("api/import")]
public class ImportController(RecipeImportService importService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Import a recipe from an external page. Requires an account: this makes the
    /// server fetch a URL of the caller's choosing, which is not something to
    /// leave open to anonymous traffic.
    /// </summary>
    [HttpPost("url")]
    [Authorize]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ImportFromUrl(
        [FromBody] ImportRecipeFromUrlRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            return Unauthorized();

        var result = await importService.ImportAsync(request.Url, userId, ct);

        return result.Outcome switch
        {
            ImportOutcome.Created => CreatedAtAction(
                nameof(RecipesController.GetRecipeById), "Recipes",
                new { id = result.RecipeId }, result.RecipeId),

            // 409 with the existing id, so a repeated click lands the caller on
            // the recipe rather than on an error.
            ImportOutcome.Duplicate => Conflict(new
            {
                message = "That URL has already been imported.",
                recipeId = result.RecipeId
            }),

            ImportOutcome.InvalidUrl or ImportOutcome.BlockedHost => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: result.Error ?? "That URL cannot be imported."),

            // 422: the page was fetched fine, it just is not a recipe.
            ImportOutcome.NoRecipeFound => Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: result.Error ?? "Could not extract a recipe from that URL."),

            // 502: the upstream site failed, not this API and not the caller.
            _ => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: result.Error ?? "Could not reach that URL.")
        };
    }
}
