using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApi.Models;

/// <summary>
/// 短信发送记录表
/// </summary>
public sealed class SmsSendRecord : BaseEntity
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
    /// 目标号码
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string TargetNumber { get; set; } = string.Empty;

    /// <summary>
    /// 短信内容
    /// </summary>
    [Required]
    [Column(TypeName = "text")]
    public string MessageContent { get; set; } = string.Empty;

    /// <summary>
    /// 发送状态：Pending=待发送, Sending=发送中, Success=成功, Failed=失败
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// 发送时间（实际发送时间）
    /// </summary>
    [Column(TypeName = "timestamp with time zone")]
    public DateTime? SentTime { get; set; }

    /// <summary>
    /// 错误信息（发送失败时记录）
    /// </summary>
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 触发来源：Manual=手动, API=API触发, Scheduled=定时任务
    /// </summary>
    [MaxLength(20)]
    public string? TriggerSource { get; set; }

    /// <summary>
    /// 触发API的URL（预留字段）
    /// </summary>
    [MaxLength(500)]
    public string? TriggerApiUrl { get; set; }
}
