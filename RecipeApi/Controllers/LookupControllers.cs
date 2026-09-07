using Microsoft.AspNetCore.Mvc;
using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;

namespace RecipeApi.Controllers;

[ApiController]
[Route("api/cuisines")]
public class CuisinesController(ILookupRepository lookup) : ControllerBase
{
    /// <summary>All cuisines with recipe counts, for populating the filter UI.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CuisineDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CuisineDto>>> GetAll(CancellationToken ct) =>
        Ok(await lookup.GetCuisinesAsync(ct));
}

[ApiController]
[Route("api/ingredients")]
public class IngredientsController(ILookupRepository lookup) : ControllerBase
{
    /// <summary>Ingredient list, optionally filtered for autocomplete.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<IngredientDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IngredientDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await lookup.SearchIngredientsAsync(search, limit, ct));
}
