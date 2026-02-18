using System.ComponentModel.DataAnnotations;

namespace WebApi.Models;

/// <summary>
/// 用户已读回执表：用于维护短信/来电的未读红点数字。
/// 一条记录表示某个用户已读某条消息(短信/来电)。
/// </summary>
public sealed class MessageReadReceipt : BaseEntity
{
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// 消息类型：Sms 或 Hangup
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string MessageType { get; set; } = string.Empty;

    /// <summary>
    /// 对应的消息ID（SmsMessage.Id 或 CallHangupRecord.Id）
    /// </summary>
    [Required]
    public Guid SourceId { get; set; }

    /// <summary>
    /// 已读时间（UTC）
    /// </summary>
    public DateTime ReadTimeUtc { get; set; }
}
