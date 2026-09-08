namespace RecipeApi.Application.Dtos;

public static class Paging
{
    public const int MaxPageSize = 100;

    /// <summary>
    /// One home for the clamping rule, so every paged endpoint behaves the same.
    /// Page is clamped as well as PageSize: page 0 produces a negative Skip,
    /// which throws rather than returning an empty result.
    /// </summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));
}
