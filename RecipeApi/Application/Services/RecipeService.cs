using RecipeApi.Application.Dtos;
using RecipeApi.Application.Interfaces;
using RecipeApi.Domain;

namespace RecipeApi.Application.Services;

public class RecipeService(IRecipeRepository repository)
{
    public Task<PagedResult<RecipeSummaryDto>> SearchAsync(
        RecipeSearchQuery query, Guid? currentUserId, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var safe = query with { Page = page, PageSize = pageSize };

        return repository.SearchAsync(safe, currentUserId, ct);
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
