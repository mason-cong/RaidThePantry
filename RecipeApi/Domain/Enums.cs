namespace RecipeApi.Domain;

public enum DifficultyLevel
{
    Easy = 0,
    Medium = 1,
    Hard = 2
}

public enum RecipeSourceType
{
    /// <summary>Created by a signed-in user through POST /api/recipes.</summary>
    Manual = 0,

    /// <summary>Imported synchronously by a user through POST /api/import/url.</summary>
    UrlImport = 1,

    /// <summary>Promoted out of staging.ScrapedPage by the Worker's promote command.</summary>
    BulkScrape = 2
}
