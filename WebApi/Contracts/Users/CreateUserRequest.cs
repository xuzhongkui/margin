using System.ComponentModel.DataAnnotations;
using WebApi.Models;

namespace WebApi.Contracts.Users;

public sealed class CreateUserRequest
{
    [Required]
    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    [MaxLength(500)]
    public string? Remark { get; set; }
}
