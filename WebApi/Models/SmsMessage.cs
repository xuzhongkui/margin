using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApi.Models;

/// <summary>
/// 短信记录表
/// </summary>
public sealed class SmsMessage : BaseEntity
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
    /// 来信号码
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string SenderNumber { get; set; } = string.Empty;

    /// <summary>
    /// 短信内容
    /// </summary>
    [Required]
    [Column(TypeName = "text")]
    public string MessageContent { get; set; } = string.Empty;

    /// <summary>
    /// 接收时间（短信实际接收时间）
    /// </summary>
    [Column(TypeName = "timestamp with time zone")]
    public DateTime ReceivedTime { get; set; }

    /// <summary>
    /// 短信时间戳（从短信头部解析的时间）
    /// </summary>
    [MaxLength(100)]
    public string? SmsTimestamp { get; set; }

    /// <summary>
    /// 运营商名称（从设备端口信息获取）
    /// </summary>
    [MaxLength(100)]
    public string? Operator { get; set; }
}
