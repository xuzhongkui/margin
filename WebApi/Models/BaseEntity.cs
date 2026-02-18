using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApi.Models;

public abstract class BaseEntity
{
    [Key]
    public Guid Id { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreateTime { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime UpdateTime { get; set; }

    public bool IsDelete { get; set; }

    [MaxLength(500)]
    public string? Remark { get; set; }
}
