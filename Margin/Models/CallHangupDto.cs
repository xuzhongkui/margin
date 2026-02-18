namespace Margin.Models;

/// <summary>
/// 来电挂断上报数据传输对象（边缘端 -> 服务端）
/// </summary>
public sealed class CallHangupDto
{
    /// <summary>
    /// COM口名称（如 COM3）
    /// </summary>
    public string ComPort { get; set; } = string.Empty;

    /// <summary>
    /// 来电号码（+CLIP 解析到的号码；可能为空/未知）
    /// </summary>
    public string? CallerNumber { get; set; }

    /// <summary>
    /// 挂断时间（UTC）
    /// </summary>
    public DateTime HangupTimeUtc { get; set; }

    /// <summary>
    /// 挂断原因/来源（例如 AutoHangup、Manual、Unknown）
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 原始串口片段（用于排查不同模块上报差异；可为空）
    /// </summary>
    public string? RawLine { get; set; }
}
