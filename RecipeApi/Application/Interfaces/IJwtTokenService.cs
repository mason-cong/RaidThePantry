using RecipeApi.Application.Dtos;
using RecipeApi.Domain;

namespace RecipeApi.Application.Interfaces;

public interface IJwtTokenService
{
    AuthResponse CreateToken(ApplicationUser user);
}
