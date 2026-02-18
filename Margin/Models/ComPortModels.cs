namespace Margin.Models;

public class ComPortScanResult
{
    public DateTime ScanTime { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<ComPortInfo> AvailablePorts { get; set; } = new();
}

public class ComPortInfo
{
    public string PortName { get; set; } = string.Empty;
    public int? BaudRate { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsSmsModem { get; set; }
    public string? ModemResponse { get; set; }
    public string? ErrorMessage { get; set; }

    // 短信猫详细信息
    public ModemDetails? ModemInfo { get; set; }
}

public class ModemDetails
{
    /// <summary>
    /// 设备制造商 (AT+CGMI)
    /// </summary>
    public string? Manufacturer { get; set; }

    /// <summary>
    /// 设备型号 (AT+CGMM)
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 固件版本 (AT+CGMR)
    /// </summary>
    public string? FirmwareVersion { get; set; }

    /// <summary>
    /// IMEI号码 (AT+CGSN)
    /// </summary>
    public string? IMEI { get; set; }

    /// <summary>
    /// ICCID (SIM 卡号)。优先 AT+CCID，必要时 fallback 到 AT+ICCID/AT^ICCID。
    /// </summary>
    public string? ICCID { get; set; }

    /// <summary>
    /// SIM卡状态 (AT+CPIN?)
    /// </summary>
    public string? SimStatus { get; set; }

    /// <summary>
    /// 是否插入SIM卡
    /// </summary>
    public bool HasSimCard { get; set; }

    /// <summary>
    /// 运营商名称 (AT+COPS?)
    /// </summary>
    public string? Operator { get; set; }

    /// <summary>
    /// 信号强度 (AT+CSQ) 0-31, 99表示未知
    /// </summary>
    public int? SignalStrength { get; set; }

    /// <summary>
    /// 信号质量描述
    /// </summary>
    public string? SignalQuality { get; set; }

    /// <summary>
    /// 网络注册状态 (AT+CREG?)
    /// </summary>
    public string? NetworkStatus { get; set; }

    /// <summary>
    /// 电话号码 (AT+CNUM)
    /// </summary>
    public string? PhoneNumber { get; set; }
}
