using System.ComponentModel.DataAnnotations;

namespace WebApi.Contracts.Users;

public sealed class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
