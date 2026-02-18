using WebApi.Models;

namespace WebApi.Services.Auth;

public interface IRefreshTokenService
{
    Task<string> CreateAsync(User user, CancellationToken cancellationToken);
    Task<User?> ValidateAsync(string refreshToken, CancellationToken cancellationToken);
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}
