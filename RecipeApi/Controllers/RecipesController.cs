using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Application.Services;

namespace RecipeApi.Controllers;

[ApiController]
[Route("api/recipes")]
public class RecipesController(RecipeService recipeService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>Search, filter and paginate recipes. Anonymous.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<RecipeSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<RecipeSummaryDto>>> Search(
        [FromQuery] RecipeSearchRequestParams request, CancellationToken ct)
    {
        var result = await recipeService.SearchAsync(request.ToSearchQuery(), currentUser.UserId, ct);
        return Ok(result);
    }

    /// <summary>Full detail for one recipe. Anonymous.</summary>
    [HttpGet("{id:guid}", Name = nameof(GetRecipeById))]
    [ProducesResponseType<RecipeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecipeDetailDto>> GetRecipeById(Guid id, CancellationToken ct)
    {
        var recipe = await recipeService.GetByIdAsync(id, currentUser.UserId, ct);
        return recipe is null ? NotFound() : Ok(recipe);
    }

    /// <summary>Create a recipe. Requires an account; the caller becomes its owner.</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Guid>> Create([FromBody] CreateRecipeRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            return Unauthorized();

        var id = await recipeService.CreateAsync(request, userId, ct);
        return CreatedAtAction(nameof(GetRecipeById), new { id }, id);
    }

    /// <summary>Replace a recipe you created. Scraped recipes have no owner and cannot be edited.</summary>
    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateRecipeRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            return Unauthorized();

        return ToResponse(await recipeService.UpdateAsync(id, request, userId, ct));
    }

    /// <summary>Delete a recipe you created.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            return Unauthorized();

        return ToResponse(await recipeService.DeleteAsync(id, userId, ct));
    }

    private IActionResult ToResponse(WriteResult result) => result switch
    {
        WriteResult.Success => NoContent(),
        WriteResult.NotFound => NotFound(),
        // 403 rather than 404: the recipe demonstrably exists, and pretending
        // otherwise would make a genuine 404 indistinguishable from a permission
        // problem for the person debugging it.
        WriteResult.Forbidden => Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "You can only modify recipes you created."),
        _ => throw new InvalidOperationException($"Unhandled {nameof(WriteResult)}: {result}")
    };
}
