namespace Margin.Models;

/// <summary>
/// 短信接收数据传输对象
/// </summary>
public class SmsReceivedDto
{
    /// <summary>
    /// 设备ID
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// COM口名称
    /// </summary>
    public string ComPort { get; set; } = string.Empty;

    /// <summary>
    /// 来信号码
    /// </summary>
    public string SenderNumber { get; set; } = string.Empty;

    /// <summary>
    /// 短信内容
    /// </summary>
    public string MessageContent { get; set; } = string.Empty;

    /// <summary>
    /// 接收时间
    /// </summary>
    public DateTime ReceivedTime { get; set; }

    /// <summary>
    /// 短信时间戳（原始格式）
    /// </summary>
    public string? SmsTimestamp { get; set; }
}
