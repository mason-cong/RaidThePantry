using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RecipeApi.Infrastructure.Persistence;

/// <summary>
/// Translates provider-specific failures into something the repositories can
/// branch on. Kept in one place so the SQLSTATE literals do not get copied
/// around, and so nothing outside Infrastructure ever sees an Npgsql type.
/// </summary>
internal static class PostgresErrors
{
    /// <summary>23505 is PostgreSQL's unique_violation.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    /// <summary>Narrows to a specific index or constraint by name.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException { SqlState: "23505" } pg &&
        (pg.ConstraintName?.Contains(constraintName, StringComparison.OrdinalIgnoreCase) ?? false);
}
