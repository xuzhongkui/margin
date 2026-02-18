namespace WebApi.Contracts.DeviceCom;

public sealed record DeviceComPortDto
{
    public string? DeviceId { get; init; }
    public string? PortName { get; init; }

    public bool? IsAvailable { get; init; }
    public bool? IsSmsModem { get; init; }

    public DeviceModemInfoDto? ModemInfo { get; init; }

    // 兼容未知格式/解析失败时的原始字符串
    public string? Raw { get; init; }
}

public sealed record DeviceModemInfoDto
{
    public bool? HasSimCard { get; init; }
    public string? Iccid { get; init; }
    public string? Operator { get; init; }
    public int? SignalStrength { get; init; }
    public string? SignalQuality { get; init; }
    public string? PhoneNumber { get; init; }

    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? Imei { get; init; }
    public string? SimStatus { get; init; }
    public string? NetworkStatus { get; init; }
}
