using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;

namespace RecipeApi.Application.Services;

public class RecipeService(IRecipeRepository repository)
{
    public const int MaxPageSize = 100;

    public Task<PagedResult<RecipeSummaryDto>> SearchAsync(RecipeSearchQuery query, CancellationToken ct)
    {
        // Page is clamped as well as PageSize: Page = 0 produces a negative Skip,
        // which throws rather than returning an empty result.
        var safe = query with
        {
            Page = Math.Max(query.Page, 1),
            PageSize = Math.Clamp(query.PageSize, 1, MaxPageSize)
        };

        return repository.SearchAsync(safe, ct);
    }

    public Task<RecipeDetailDto?> GetByIdAsync(Guid id, Guid? currentUserId, CancellationToken ct) =>
        repository.GetByIdAsync(id, currentUserId, ct);
}
