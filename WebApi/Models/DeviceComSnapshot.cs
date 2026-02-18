using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApi.Models;

public sealed class DeviceComSnapshot : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "text")]
    public string DataJson { get; set; } = "[]";
}
