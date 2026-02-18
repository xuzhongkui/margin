using System.ComponentModel.DataAnnotations;
using WebApi.Models;

namespace WebApi.Contracts.Users;

public sealed class UpdateUserRequest
{
    [Required]
    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    [MaxLength(500)]
    public string? Remark { get; set; }

    [MinLength(6)]
    public string? NewPassword { get; set; }
}
