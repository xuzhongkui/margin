namespace WebApi.Contracts.Users;

public sealed class LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public UserResponse User { get; init; } = new UserResponse();
}
