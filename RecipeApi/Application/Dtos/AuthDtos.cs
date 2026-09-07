using System.ComponentModel.DataAnnotations;

namespace RecipeApi.Application.Dtos;

// Validation attributes go on the record's primary-constructor parameters, with
// no [property:] target. MVC binds and validates records through the constructor
// and throws if it finds validation metadata on the generated properties instead.
public record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    // Must match Identity's Password.RequiredLength in AuthServiceCollectionExtensions,
    // otherwise a short password passes model validation only to be rejected later
    // with a differently-shaped error.
    [Required, MinLength(10), MaxLength(128)] string Password);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt);

public record CurrentUserDto(Guid Id, string Email, DateTimeOffset CreatedAt);
