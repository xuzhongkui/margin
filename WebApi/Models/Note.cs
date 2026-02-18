using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApi.Models;

/// <summary>
/// 记事本表
/// </summary>
public sealed class Note : BaseEntity
{
    /// <summary>
    /// 标题
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 内容（富文本）
    /// </summary>
    [Column(TypeName = "text")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID（关联用户）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 标签（可选，用于分类）
    /// </summary>
    [MaxLength(100)]
    public string? Tags { get; set; }

    /// <summary>
    /// 是否置顶
    /// </summary>
    public bool IsPinned { get; set; }
}
