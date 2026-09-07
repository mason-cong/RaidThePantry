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
        var result = await recipeService.SearchAsync(request.ToSearchQuery(), ct);
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
}
