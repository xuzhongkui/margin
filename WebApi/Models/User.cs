using System.ComponentModel.DataAnnotations;

namespace WebApi.Models;

public sealed class User : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string PasswordSalt { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;
}
