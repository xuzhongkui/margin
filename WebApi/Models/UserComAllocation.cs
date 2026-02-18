using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApi.Models;

public sealed class UserComAllocation : BaseEntity
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(200)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "text")]
    public string ComListJson { get; set; } = "[]";

    // 导航属性
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
