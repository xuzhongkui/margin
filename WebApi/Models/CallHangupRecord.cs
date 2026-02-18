using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApi.Models;

/// <summary>
/// 来电自动挂断记录
/// </summary>
public sealed class CallHangupRecord : BaseEntity
{
    /// <summary>
    /// 设备ID（边缘设备标识）
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// COM口名称（如 COM3）
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ComPort { get; set; } = string.Empty;

    /// <summary>
    /// 来电号码（+CLIP 解析到的号码；可能为空/未知）
    /// </summary>
    [MaxLength(50)]
    public string? CallerNumber { get; set; }

    /// <summary>
    /// 挂断时间（UTC）
    /// </summary>
    [Column(TypeName = "timestamp with time zone")]
    public DateTime HangupTime { get; set; }

    /// <summary>
    /// 挂断原因/来源（例如 AutoHangup、Manual、Unknown）
    /// </summary>
    [MaxLength(50)]
    public string? Reason { get; set; }

    /// <summary>
    /// 原始串口片段/上报内容（便于排查不同模块的上报差异）
    /// </summary>
    [Column(TypeName = "text")]
    public string? RawLine { get; set; }
}
