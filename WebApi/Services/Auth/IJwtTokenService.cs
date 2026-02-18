using WebApi.Models;

namespace WebApi.Services.Auth;

public interface IJwtTokenService
{
    string CreateToken(User user);
    string CreateToken(User user, DateTime expiresAtUtc);
}
