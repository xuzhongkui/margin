namespace WebApi.Services;

/// <summary>
/// 来电挂断上报数据传输对象（边缘端 -> 服务端）
/// </summary>
public sealed class CallHangupDto
{
    public string ComPort { get; set; } = string.Empty;
    public string? CallerNumber { get; set; }
    public DateTime HangupTimeUtc { get; set; }
    public string? Reason { get; set; }
    public string? RawLine { get; set; }
}
