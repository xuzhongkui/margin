namespace WebApi.Services.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int ExpireMinutes { get; set; } = 120;
    public int RefreshTokenDays { get; set; } = 7;
}
