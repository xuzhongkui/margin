using System.ComponentModel.DataAnnotations;

namespace WebApi.Contracts.Users;

public sealed class LoginRequest
{
    [Required]
    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
