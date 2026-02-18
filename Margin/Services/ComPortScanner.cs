using System.IO.Ports;
using System.Text;
using System.Text.Json;
using Margin.Models;

namespace Margin.Services;

/// <summary>
/// Service for scanning COM ports and detecting SMS modems
/// </summary>
public class ComPortScanner
{
    private readonly ILogger<ComPortScanner> _logger;
    private readonly IConfiguration _configuration;

    public ComPortScanner(ILogger<ComPortScanner> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Scan all available COM ports
    /// </summary>
    public Task<ComPortScanResult> ScanComPortsAsync()
    {
        return ScanComPortsAsync(onPortFound: null);
    }

    /// <summary>
    /// Scan all available COM ports, and optionally report each port as soon as it is scanned.
    /// </summary>
    public async Task<ComPortScanResult> ScanComPortsAsync(Action<ComPortInfo>? onPortFound)
    {
        _logger.LogInformation("Starting COM port scan...");

        var result = new ComPortScanResult
        {
            ScanTime = DateTime.UtcNow,
            AvailablePorts = new List<ComPortInfo>()
        };

        try
        {
            var portNames = SerialPort.GetPortNames();
            _logger.LogInformation($"Found {portNames.Length} COM ports");

            foreach (var portName in portNames)
            {
                var portInfo = new ComPortInfo
                {
                    PortName = portName,
                    IsAvailable = false,
                    IsSmsModem = false
                };

                try
                {
                    _logger.LogInformation($"🔍 Testing {portName}...");

                    // 尝试多个波特率（优先使用配置；格式："115200,9600,19200"）
                    var baudRates = GetConfiguredBaudRates();
                    bool modemDetected = false;

                    foreach (var baudRate in baudRates)
                    {
                        try
                        {
                            _logger.LogInformation($"  Trying {portName} at {baudRate} baud...");

                            using var port = new SerialPort(portName)
                            {
                                BaudRate = baudRate,
                                DataBits = 8,
                                StopBits = StopBits.One,
                                Parity = Parity.None,
                                ReadTimeout = 3000,
                                WriteTimeout = 3000,
                                DtrEnable = true,
                                RtsEnable = true
                            };

                            port.Open();
                            portInfo.IsAvailable = true;

                            // 清空缓冲区，避免历史 URC/回显干扰第一次 AT 探测
                            port.DiscardInBuffer();
                            port.DiscardOutBuffer();

                            // 等待端口稳定（DTR/RTS 置位后部分模块需要一点时间）
                            await Task.Delay(300);

                            // 一些 USB-Serial/驱动组合下，ATLib 在探测/详情阶段容易卡死或超时。
                            // 这里全程使用 SerialPort 级别的 AT 指令交互来做探测与详情读取。
                            var probe = await ProbeAtAsync(port, portName, baudRate, attempts: 3, timeoutPerAttemptMs: 1500);
                            if (!probe.Success)
                            {
                                portInfo.ModemResponse = probe.RawResponse;
                                port.Close();
                                continue;
                            }

                            portInfo.IsSmsModem = true;
                            portInfo.BaudRate = baudRate;
                            portInfo.ModemResponse = probe.RawResponse;
                            modemDetected = true;
                            _logger.LogInformation($"✅ SMS modem detected on {portName} at {baudRate} baud");

                            // 先推送一次“已识别为短信猫”，避免后续获取详情阻塞导致前端长时间无增量。
                            SafeInvokeOnPortFound(onPortFound, portInfo);

                            // 获取短信猫详细信息（纯 SerialPort AT 指令交互 + 总超时保护，避免库层卡死）
                            portInfo.ModemInfo = await WithTimeoutAsync(
                                () => GetModemDetailsAsync(port),
                                timeout: TimeSpan.FromSeconds(25),
                                onTimeout: () => _logger.LogWarning($"Timeout getting modem details on {portName} at {baudRate} baud"));

                            // 推送带详情的更新（前端会按 deviceId+portName upsert，不会重复累加）
                            SafeInvokeOnPortFound(onPortFound, portInfo);

                            port.Close();

                            if (modemDetected)
                            {
                                break; // 找到正确的波特率，退出循环
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"  {portName} at {baudRate} baud failed: {ex.Message}");
                        }
                    }

                    if (!modemDetected && portInfo.IsAvailable)
                    {
                        _logger.LogInformation($"❌ {portName} is available but not a modem (tried all baud rates)");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    portInfo.ErrorMessage = "Port is in use by another application";
                    _logger.LogWarning($"Port {portName} is in use");
                }
                catch (Exception ex)
                {
                    portInfo.ErrorMessage = ex.Message;
                    _logger.LogWarning($"Error scanning port {portName}: {ex.Message}");
                }

                result.AvailablePorts.Add(portInfo);

                // Push incrementally to caller (e.g., SignalR) to avoid long wait for full scan.
                try
                {
                    onPortFound?.Invoke(portInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"onPortFound callback failed for {portInfo.PortName}: {ex.Message}");
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during COM port scan");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Convert scan result to JSON string
    /// </summary>
    public string SerializeScanResult(ComPortScanResult result)
    {
        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static void SafeInvokeOnPortFound(Action<ComPortInfo>? onPortFound, ComPortInfo portInfo)
    {
        if (onPortFound == null)
        {
            return;
        }

        try
        {
            onPortFound(portInfo);
        }
        catch
        {
            // callback 不能影响扫描主流程
        }
    }

    private static async Task<T?> WithTimeoutAsync<T>(Func<Task<T>> action, TimeSpan timeout, Action onTimeout)
    {
        var task = action();
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            onTimeout();
            return default;
        }

        return await task;
    }

    private sealed record ProbeResult(bool Success, string RawResponse);

    private async Task<ProbeResult> ProbeAtAsync(
        SerialPort port,
        string portName,
        int baudRate,
        int attempts,
        int timeoutPerAttemptMs)
    {
        // 目标：在不依赖 ATLib 的情况下，尽可能可靠地判断“这是不是 AT 设备”。
        // 只要看到 OK / ERROR / +CME ERROR / +CMS ERROR 之一就认为串口响应正常。
        // 有些设备要求 \r，有些要求 \r\n，这里两种都试。

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                _logger.LogInformation($"    Attempt {attempt}/{attempts}: Probing AT via SerialPort...");

                port.DiscardInBuffer();
                port.DiscardOutBuffer();

                var raw = await SendAndCollectUntilAsync(port, "AT\r", timeoutPerAttemptMs);
                if (LooksLikeAtResponse(raw))
                {
                    return new ProbeResult(true, raw);
                }

                // fallback: some devices require CRLF
                raw = raw + await SendAndCollectUntilAsync(port, "AT\r\n", timeoutPerAttemptMs);
                if (LooksLikeAtResponse(raw))
                {
                    return new ProbeResult(true, raw);
                }

                _logger.LogInformation($"    No OK/ERROR response (attempt {attempt}/{attempts})");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"    Probe failed on {portName} at {baudRate} baud (attempt {attempt}/{attempts}): {ex.Message}");
            }
        }

        return new ProbeResult(false, string.Empty);
    }

    private static bool LooksLikeAtResponse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Contains("\r\nOK\r\n", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("\nOK\n", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("\rOK\r", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("+CME ERROR", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("+CMS ERROR", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SendAndCollectUntilAsync(SerialPort port, string command, int timeoutMs)
    {
        var sb = new StringBuilder();
        port.Write(command);

        var start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs)
        {
            await Task.Delay(50);
            try
            {
                var chunk = port.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    sb.Append(chunk);
                    var text = sb.ToString();
                    if (LooksLikeAtResponse(text))
                    {
                        break;
                    }
                }
            }
            catch (TimeoutException)
            {
                // ignore and keep polling until overall timeout
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取短信猫详细信息（不依赖 ATLib，避免库层卡死/阻塞）
    /// </summary>
    private async Task<ModemDetails> GetModemDetailsAsync(SerialPort port)
    {
        var details = new ModemDetails();

        try
        {
            // 尽量关闭回显，避免把 "AT+XXX" 混入解析（并非所有模块都支持/允许）。
            _ = await SendAtAndGetPayloadAsync(port, "ATE0", timeoutMs: 1500);

            details.Manufacturer = await SendAtAndGetPayloadAsync(port, "AT+CGMI", timeoutMs: 3000);
            details.Model = await SendAtAndGetPayloadAsync(port, "AT+CGMM", timeoutMs: 3000);
            details.FirmwareVersion = await SendAtAndGetPayloadAsync(port, "AT+CGMR", timeoutMs: 3000);
            details.IMEI = await SendAtAndGetPayloadAsync(port, "AT+CGSN", timeoutMs: 3000);

            var simStatus = await SendAtAndGetPayloadAsync(port, "AT+CPIN?", timeoutMs: 5000);
            details.SimStatus = simStatus;
            details.HasSimCard = !string.IsNullOrEmpty(simStatus) &&
                                 (simStatus.Contains("READY", StringComparison.OrdinalIgnoreCase) ||
                                  simStatus.Contains("SIM PIN", StringComparison.OrdinalIgnoreCase));

            var operatorInfo = await SendAtAndGetPayloadAsync(port, "AT+COPS?", timeoutMs: 5000);
            if (!string.IsNullOrEmpty(operatorInfo))
            {
                // 解析运营商名称，格式: +COPS: 0,0,"CHINA MOBILE",7
                var match = System.Text.RegularExpressions.Regex.Match(operatorInfo, "\"([^\"]+)\"");
                if (match.Success)
                {
                    details.Operator = match.Groups[1].Value;
                }
            }

            var signalInfo = await SendAtAndGetPayloadAsync(port, "AT+CSQ", timeoutMs: 3000);
            if (!string.IsNullOrEmpty(signalInfo))
            {
                var match = System.Text.RegularExpressions.Regex.Match(signalInfo, @"\+CSQ:\s*(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int signal))
                {
                    details.SignalStrength = signal;
                    details.SignalQuality = GetSignalQualityDescription(signal);
                }
            }

            var networkStatus = await SendAtAndGetPayloadAsync(port, "AT+CREG?", timeoutMs: 3000);
            if (!string.IsNullOrEmpty(networkStatus))
            {
                var match = System.Text.RegularExpressions.Regex.Match(networkStatus, @"\+CREG:\s*\d+,(\d+)");
                if (match.Success)
                {
                    var status = match.Groups[1].Value;
                    details.NetworkStatus = status switch
                    {
                        "0" => "Not registered",
                        "1" => "Registered (Home)",
                        "2" => "Searching",
                        "3" => "Registration denied",
                        "5" => "Registered (Roaming)",
                        _ => $"Unknown ({status})"
                    };
                }
            }

            if (details.HasSimCard)
            {
                // 部分模块在未 READY 时会 ERROR/超时；这里在确认有 SIM 后再读 ICCID，避免无谓等待。
                details.ICCID = await TryGetIccidAsync(port);
            }

            var phoneNumber = await SendAtAndGetPayloadAsync(port, "AT+CNUM", timeoutMs: 5000);
            if (!string.IsNullOrEmpty(phoneNumber))
            {
                var match = System.Text.RegularExpressions.Regex.Match(phoneNumber, "\"(\\+?\\d+)\"");
                if (match.Success)
                {
                    details.PhoneNumber = match.Groups[1].Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error getting modem details: {ex.Message}");
        }

        return details;
    }

    private static async Task<string?> SendAtAndGetPayloadAsync(SerialPort port, string command, int timeoutMs)
    {
        var raw = await SendAtAndCollectRawAsync(port, command, timeoutMs);
        return ExtractPayloadFromAtResponse(raw, command);
    }

    private static async Task<string> SendAtAndCollectRawAsync(SerialPort port, string command, int timeoutMs)
    {
        // 使用 CR 作为结束符；部分设备接受 CRLF，但 CR 更通用。
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        port.Write(command + "\r");

        var sb = new StringBuilder();
        var start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs)
        {
            await Task.Delay(50);
            var chunk = port.ReadExisting();
            if (!string.IsNullOrEmpty(chunk))
            {
                sb.Append(chunk);
                var text = sb.ToString();
                if (LooksLikeAtResponse(text))
                {
                    break;
                }
            }
        }

        return sb.ToString();
    }

    private static string? ExtractPayloadFromAtResponse(string raw, string command)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // 统一按行拆分，过滤回显/OK/ERROR，剩余行拼成 payload。
        var lines = raw
            .Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        lines.RemoveAll(l => l.Equals("OK", StringComparison.OrdinalIgnoreCase));
        lines.RemoveAll(l => l.Equals("ERROR", StringComparison.OrdinalIgnoreCase) || l.StartsWith("+CME ERROR", StringComparison.OrdinalIgnoreCase) || l.StartsWith("+CMS ERROR", StringComparison.OrdinalIgnoreCase));
        lines.RemoveAll(l => l.Equals(command, StringComparison.OrdinalIgnoreCase));

        return lines.Count > 0 ? string.Join(" ", lines) : null;
    }

    private async Task<string?> TryGetIccidAsync(SerialPort port)
    {
        // 常见模块支持：AT+CCID；部分模块用 AT+ICCID 或厂商命令 AT^ICCID。
        // 这里做多策略尝试，并把返回规整为纯数字（常见 19/20 位）。
        var candidates = new[] { "AT+CCID", "AT+ICCID", "AT^ICCID" };
        foreach (var cmd in candidates)
        {
            try
            {
                var payload = await SendAtAndGetPayloadAsync(port, cmd, timeoutMs: 5000);
                var normalized = NormalizeIccid(payload);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }
            catch
            {
                // 单个命令失败不影响后续 fallback
            }
        }

        return null;
    }

    private static string? NormalizeIccid(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        // 典型返回："+CCID: 8986..." / "8986..." / "\"8986...\""。
        // 提取最长的连续数字串作为 ICCID。
        var match = System.Text.RegularExpressions.Regex.Match(payload, @"(\d{18,22})");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        // fallback：去掉非数字后再判定长度
        var digits = new string(payload.Where(char.IsDigit).ToArray());
        if (digits.Length >= 18 && digits.Length <= 22)
        {
            return digits;
        }

        return null;
    }

    private int[] GetConfiguredBaudRates()
    {
        // 配置项：ComPortScanner:BaudRates，例："115200,9600,19200"
        var configured = _configuration["ComPortScanner:BaudRates"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var parsed = configured
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .Distinct()
                .ToArray();

            if (parsed.Length > 0)
            {
                return parsed;
            }
        }

        // 默认值（保持原来行为）
        return new[] { 115200, 9600, 19200, 38400, 57600 };
    }

    /// <summary>
    /// 获取信号质量描述
    /// </summary>
    private string GetSignalQualityDescription(int signal)
    {
        return signal switch
        {
            0 or 99 => "No Signal",
            >= 1 and <= 9 => "Very Weak",
            >= 10 and <= 14 => "Weak",
            >= 15 and <= 19 => "Fair",
            >= 20 and <= 24 => "Good",
            >= 25 and <= 31 => "Excellent",
            _ => "Unknown"
        };
    }
}
