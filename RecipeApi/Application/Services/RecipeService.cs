using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;

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

    /// <summary>
    /// Manual creation. SourceUrl stays null — only the importer sets one, and the
    /// filtered unique index on it would otherwise reject a second manual recipe.
    /// </summary>
    public Task<Guid> CreateAsync(CreateRecipeRequest request, Guid userId, CancellationToken ct) =>
        repository.CreateAsync(request, userId, RecipeSourceType.Manual, sourceUrl: null, ct);

    public Task<WriteResult> UpdateAsync(Guid id, CreateRecipeRequest request, Guid userId, CancellationToken ct) =>
        repository.UpdateAsync(id, request, userId, ct);

    public Task<WriteResult> DeleteAsync(Guid id, Guid userId, CancellationToken ct) =>
        repository.DeleteAsync(id, userId, ct);
}
