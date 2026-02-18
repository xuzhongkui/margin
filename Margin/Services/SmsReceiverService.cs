using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace Margin.Services;

/// <summary>
/// 短信接收监听服务 - 支持多端口并发监听
/// </summary>
public class SmsReceiverService : IDisposable
{
    private readonly ILogger<SmsReceiverService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, PortListener> _portListeners = new();

    // 来电自动挂断：避免重复触发/并发写串口
    private readonly ConcurrentDictionary<string, DateTime> _lastAutoHangupUtc = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _portCommandLocks = new();

    // 配置项：Margin:IncomingCallAutoHangup
    private const string IncomingCallConfigSection = "Margin:IncomingCallAutoHangup";
    private const bool DefaultIncomingCallEnabled = true;
    private static readonly TimeSpan DefaultHangupDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan DefaultHangupCooldown = TimeSpan.FromSeconds(5);

    private bool _isRunning;


    // 短信接收事件
    public event Action<Margin.Models.SmsReceivedDto>? OnSmsReceived;


    // 来电挂断事件（用于上报到服务端）
    public event Action<Margin.Models.CallHangupDto>? OnCallHangup;


    private class PortListener
    {
        public SerialPort SerialPort { get; set; } = null!;
        public StringBuilder Buffer { get; } = new();
        public CancellationTokenSource CancellationTokenSource { get; } = new();
    }

    public SmsReceiverService(ILogger<SmsReceiverService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// 启动多端口短信监听
    /// </summary>
    public async Task StartListeningAsync(List<(string PortName, int BaudRate)> ports, CancellationToken cancellationToken)
    {
        if (_isRunning)
        {
            _logger.LogWarning("SMS receiver is already running");
            return;
        }

        _isRunning = true;
        _logger.LogInformation($"📱 Starting SMS receiver for {ports.Count} port(s)...");

        var tasks = new List<Task>();

        foreach (var (portName, baudRate) in ports)
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    await StartSinglePortListeningAsync(portName, baudRate, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to start SMS receiver on {portName}");
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        // 等待所有监听任务
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 启动单个端口的短信监听
    /// </summary>
    private async Task StartSinglePortListeningAsync(string portName, int baudRate, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation($"📱 Starting SMS receiver on {portName} at {baudRate} baud...");

            var serialPort = new SerialPort(portName)
            {
                BaudRate = baudRate,
                DataBits = 8,
                StopBits = StopBits.One,
                Parity = Parity.None,
                ReadTimeout = 1500,  // 增加到1500ms，与参考脚本一致
                WriteTimeout = 3000,
                DtrEnable = true,
                RtsEnable = true,
                Encoding = Encoding.ASCII  // 改为 ASCII，与参考脚本一致
            };

            var listener = new PortListener
            {
                SerialPort = serialPort
            };

            // 先打开串口，但不注册 DataReceived 事件（避免初始化期间冲突）
            serialPort.Open();

            // 添加到监听字典
            if (!_portListeners.TryAdd(portName, listener))
            {
                _logger.LogWarning($"Port {portName} is already being monitored");
                serialPort.Close();
                serialPort.Dispose();
                return;
            }

            // 等待端口稳定
            await Task.Delay(500, cancellationToken);

            // 配置短信接收模式（此时 DataReceived 事件未注册，不会冲突）
            await InitializeSmsSettingsAsync(serialPort, portName, cancellationToken);

            // 初始化完成后，再注册数据接收事件
            serialPort.DataReceived += (sender, e) => OnDataReceived(sender, e, portName, listener.Buffer);

            _logger.LogInformation($"✅ SMS receiver started successfully on {portName}");

            // 保持监听（添加心跳日志）
            int heartbeatCounter = 0;
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                await Task.Delay(1000, cancellationToken);
                heartbeatCounter++;
                
                // 每10秒打印一次心跳日志
                if (heartbeatCounter % 10 == 0)
                {
                    _logger.LogDebug($"[{portName}] Heartbeat: Listening... (Port open: {serialPort.IsOpen}, BytesToRead: {serialPort.BytesToRead})");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to start SMS receiver on {portName}");
            throw;
        }
    }

    /// <summary>
    /// 初始化短信接收设置
    /// </summary>
    private async Task InitializeSmsSettingsAsync(SerialPort serialPort, string portName, CancellationToken cancellationToken)
    {
        try
        {
            // 关闭回显
            await SendAtCommandAsync(serialPort, "ATE0", cancellationToken);
            await Task.Delay(200, cancellationToken);

            // 设置短信格式为文本模式 (0=PDU模式, 1=文本模式)
            await SendAtCommandAsync(serialPort, "AT+CMGF=1", cancellationToken);
            await Task.Delay(200, cancellationToken);

            // 设置新短信通知模式
            // AT+CNMI=2,2,0,0,0 表示：
            // - mode=2: 缓冲未读消息
            // - mt=2: 新短信直接推送到终端（不存储），使用 +CMT: 格式
            // - bm=0: 不报告广播消息
            // - ds=0: 不报告状态报告
            // - bfr=0: 清空缓冲区
            await SendAtCommandAsync(serialPort, "AT+CNMI=2,2,0,0,0", cancellationToken);
            await Task.Delay(200, cancellationToken);

            // 设置字符集为 GSM
            await SendAtCommandAsync(serialPort, "AT+CSCS=\"GSM\"", cancellationToken);
            await Task.Delay(200, cancellationToken);

            _logger.LogInformation($"✅ SMS settings initialized successfully on {portName}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to initialize SMS settings on {portName}, will continue anyway");
        }
    }

    /// <summary>
    /// 发送AT命令并等待响应
    /// </summary>
    private async Task<string> SendAtCommandAsync(SerialPort serialPort, string command, CancellationToken cancellationToken)
    {
        if (serialPort == null || !serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open");
        }

        _logger.LogDebug($"Sending AT command: {command}");

        // 清空缓冲区（与参考脚本的 Clear-SerialBuffer 一致）
        var clearStart = Environment.TickCount;
        while (Environment.TickCount - clearStart < 200)
        {
            try
            {
                if (serialPort.BytesToRead > 0)
                {
                    serialPort.ReadLine();
                }
            }
            catch
            {
                await Task.Delay(20, cancellationToken);
            }
        }

        // 发送命令 (使用 \r 而不是 \r\n，与参考脚本保持一致)
        serialPort.Write(command + "\r");

        // 使用 ReadLine 逐行读取响应（与参考脚本的 Send-At 一致）
        var lines = new List<string>();
        var startTime = Environment.TickCount;
        var timeout = 5000; // 5秒超时

        while (Environment.TickCount - startTime < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var line = serialPort.ReadLine();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                line = line.Trim();

                // 忽略命令回显
                if (line == command)
                {
                    continue;
                }

                lines.Add(line);
                _logger.LogDebug($"AT response line: {line}");

                // 检查是否收到终止标记
                if (line == "OK" ||
                    line == "ERROR" ||
                    line.StartsWith("+CME ERROR") ||
                    line.StartsWith("+CMS ERROR"))
                {
                    var fullResponse = string.Join("\r\n", lines);
                    _logger.LogDebug($"AT command completed: {command}");
                    return fullResponse;
                }
            }
            catch (TimeoutException)
            {
                // ReadLine 超时，继续等待
                await Task.Delay(30, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error reading AT response: {ex.Message}");
                await Task.Delay(30, cancellationToken);
            }
        }

        var finalResponse = string.Join("\r\n", lines);
        _logger.LogWarning($"AT command timeout: {command}, partial response: {finalResponse}");
        return finalResponse;
    }

    private bool IsIncomingCallAutoHangupEnabled()
    {
        var enabled = _configuration.GetValue<bool?>($"{IncomingCallConfigSection}:Enabled");
        return enabled ?? DefaultIncomingCallEnabled;
    }

    private TimeSpan GetIncomingCallHangupDelay()
    {
        var ms = _configuration.GetValue<int?>($"{IncomingCallConfigSection}:HangupDelayMs");
        return ms.HasValue ? TimeSpan.FromMilliseconds(ms.Value) : DefaultHangupDelay;
    }

    private TimeSpan GetIncomingCallHangupCooldown()
    {
        var ms = _configuration.GetValue<int?>($"{IncomingCallConfigSection}:CooldownMs");
        return ms.HasValue ? TimeSpan.FromMilliseconds(ms.Value) : DefaultHangupCooldown;
    }

    private IReadOnlyList<string> GetIncomingCallWhitelist()
    {
        var list = _configuration.GetSection($"{IncomingCallConfigSection}:Whitelist").Get<string[]>();
        return list ?? Array.Empty<string>();
    }

    private bool IsWhitelistedCaller(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return false;
        }

        foreach (var allowed in GetIncomingCallWhitelist())
        {
            if (string.IsNullOrWhiteSpace(allowed))
            {
                continue;
            }

            if (number.Contains(allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private SemaphoreSlim GetPortCommandLock(string portName)
    {
        return _portCommandLocks.GetOrAdd(portName, _ => new SemaphoreSlim(1, 1));
    }

    private readonly ConcurrentDictionary<string, (string Number, DateTime Utc)> _lastClipByPort = new();

    // 用于挂断上报 RawLine：保留最近一次“来电检测时的缓冲区尾部片段”和“本次 DataReceived 原始块”。
    private readonly ConcurrentDictionary<string, (string Tail, DateTime Utc)> _lastIncomingCallTailByPort = new();
    private readonly ConcurrentDictionary<string, (string Chunk, DateTime Utc)> _lastIncomingDataChunkByPort = new();

    private static string TrimForReport(string input, int maxLen)
    {
        if (string.IsNullOrEmpty(input) || maxLen <= 0)
        {
            return string.Empty;
        }

        if (input.Length <= maxLen)
        {
            return input;
        }

        // 保留末尾更利于看最后拼接到的片段（例如 +CLIP 可能在末尾）。
        return input.Substring(input.Length - maxLen, maxLen);
    }

    private string? ResolveCallerNumber(string portName, string? callerNumber)
    {
        if (!string.IsNullOrWhiteSpace(callerNumber))
        {
            return callerNumber.Trim();
        }

        if (_lastClipByPort.TryGetValue(portName, out var cached))
        {
            // +CLIP 往往会稍晚于 RING 到达：允许短时间内从缓存补齐来电号码。
            if ((DateTime.UtcNow - cached.Utc) <= TimeSpan.FromMinutes(2))
            {
                return cached.Number;
            }
        }

        return null;
    }

    private async Task TryAutoHangupAsync(SerialPort serialPort, string portName, string? callerNumber)
    {
        if (!IsIncomingCallAutoHangupEnabled())
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var cooldown = GetIncomingCallHangupCooldown();
        if (_lastAutoHangupUtc.TryGetValue(portName, out var lastUtc) && (nowUtc - lastUtc) < cooldown)
        {
            _logger.LogDebug($"[{portName}] Auto hangup suppressed by cooldown ({cooldown.TotalMilliseconds}ms)");
            return;
        }

        var delay = GetIncomingCallHangupDelay();
        if (delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay);
            }
            catch
            {
                // ignore
            }
        }

        // 关键：RING 可能先于 +CLIP 到达，这里再解析/补齐一次 caller。
        var resolvedCaller = ResolveCallerNumber(portName, callerNumber);
        if (string.IsNullOrWhiteSpace(resolvedCaller))
        {
            _logger.LogDebug($"[{portName}] Auto hangup: caller number not available yet (no +CLIP received)");
        }

        if (IsWhitelistedCaller(resolvedCaller))
        {
            _logger.LogInformation($"[{portName}] Incoming call is whitelisted ({resolvedCaller}), skip auto hangup");
            return;
        }

        var gate = GetPortCommandLock(portName);
        await gate.WaitAsync();
        try
        {
            _lastAutoHangupUtc[portName] = nowUtc;

            // 注意：DataReceived 线程里直接读串口；这里仅写命令，不读取响应，避免与 SendAtCommandAsync 冲突
            serialPort.Write("ATH\r");

            // 一些模块不支持 ATH，补发 AT+CHUP 兼容
            await Task.Delay(150);
            serialPort.Write("AT+CHUP\r");

            _logger.LogInformation($"[{portName}] Auto hangup sent (caller={resolvedCaller ?? ""})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[{portName}] Auto hangup failed");
        }
        finally
        {
            gate.Release();
        }

        // 触发挂断上报事件（由 SignalRService 订阅并推送到服务端）
        try
        {
            string? rawLine = null;
            try
            {
                var parts = new List<string>(2);
                if (_lastIncomingCallTailByPort.TryGetValue(portName, out var tailCached) &&
                    (DateTime.UtcNow - tailCached.Utc) <= TimeSpan.FromMinutes(2) &&
                    !string.IsNullOrWhiteSpace(tailCached.Tail))
                {
                    parts.Add($"Tail: [{TrimForReport(tailCached.Tail, 512)}]");
                }

                if (_lastIncomingDataChunkByPort.TryGetValue(portName, out var chunkCached) &&
                    (DateTime.UtcNow - chunkCached.Utc) <= TimeSpan.FromMinutes(2) &&
                    !string.IsNullOrWhiteSpace(chunkCached.Chunk))
                {
                    parts.Add($"Chunk: [{TrimForReport(chunkCached.Chunk, 512)}]");
                }

                rawLine = parts.Count > 0 ? string.Join("\n", parts) : null;
            }
            catch
            {
                rawLine = null;
            }

            OnCallHangup?.Invoke(new Margin.Models.CallHangupDto
            {
                ComPort = portName,
                CallerNumber = resolvedCaller,
                HangupTimeUtc = nowUtc,
                Reason = "AutoHangup",
                RawLine = rawLine
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[{portName}] Failed to raise OnCallHangup event");
        }
    }

    private void TryHandleIncomingCall(SerialPort serialPort, string portName, StringBuilder buffer, ref string bufferContent)
    {
        if (string.IsNullOrEmpty(bufferContent))
        {
            return;
        }

        // 常见来电提示：RING / +CLIP: "number" / NO CARRIER
        if (!bufferContent.Contains("RING", StringComparison.OrdinalIgnoreCase) &&
            !bufferContent.Contains("+CLIP:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 打印更完整的来电片段，方便你直接把日志贴出来（不依赖任何解析规则）。
        // 为避免日志过大，只取末尾一段。
        var tailLen = Math.Min(bufferContent.Length, 512);
        var tail = bufferContent.Substring(bufferContent.Length - tailLen, tailLen);
        _logger.LogInformation($"[{portName}] Incoming call fragment detected (tail {tailLen}/{bufferContent.Length}): [{tail}]");

        // 给挂断上报用（RawLine 的 Tail 部分）
        _lastIncomingCallTailByPort[portName] = (tail, DateTime.UtcNow);

        string? caller = null;
        bool hasFullClip = false;
        try
        {
            // 不用正则：简单扫描 +CLIP: 后面的第一个引号内容。
            var idx = bufferContent.LastIndexOf("+CLIP:", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var after = bufferContent.AsSpan(idx);
                var firstQuote = after.IndexOf('"');
                if (firstQuote >= 0)
                {
                    var rest = after.Slice(firstQuote + 1);
                    var secondQuote = rest.IndexOf('"');
                    if (secondQuote >= 0)
                    {
                        caller = rest.Slice(0, secondQuote).ToString();
                        hasFullClip = true;

                        if (!string.IsNullOrWhiteSpace(caller))
                        {
                            _lastClipByPort[portName] = (caller.Trim(), DateTime.UtcNow);
                        }
                    }
                }
            }
        }
        catch
        {
            // ignore parsing error
        }

        // 不阻塞 DataReceived：异步触发挂断逻辑（内部会再次 ResolveCallerNumber 以防 RING 先到、CLIP 后到）
        _ = Task.Run(() => TryAutoHangupAsync(serialPort, portName, caller));

        // 清理来电相关片段，避免缓冲区长期堆积。
        // 注意：若只收到 RING 或 +CLIP 片段不完整，不要清空，避免把后续拼接所需内容丢掉。
        if (hasFullClip)
        {
            buffer.Clear();
            bufferContent = string.Empty;
        }
        else if (bufferContent.Length > 4096)
        {
            buffer.Clear();
            bufferContent = string.Empty;
        }
    }

    /// <summary>
    /// 串口数据接收事件处理
    /// </summary>
    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e, string portName, StringBuilder buffer)
    {
        try
        {
            if (sender is not SerialPort serialPort || !serialPort.IsOpen)
            {
                _logger.LogWarning($"[{portName}] DataReceived triggered but port is null or closed");
                return;
            }

            var data = serialPort.ReadExisting();

            // 给挂断上报用（RawLine 的 Chunk 部分）
            if (!string.IsNullOrEmpty(data))
            {
                _lastIncomingDataChunkByPort[portName] = (data, DateTime.UtcNow);
            }

            // 🔥🔥🔥 直接打印原始数据，看看到底收到了什么！🔥🔥🔥
            _logger.LogInformation($"🔥🔥🔥 [{portName}] 原始数据接收:");
            _logger.LogInformation($"  数据长度: {data.Length} 字符");
            _logger.LogInformation($"  字节数: {serialPort.BytesToRead}");
            _logger.LogInformation($"  原始内容: [{data}]");
            _logger.LogInformation($"  十六进制: {BitConverter.ToString(System.Text.Encoding.ASCII.GetBytes(data))}");

            if (string.IsNullOrEmpty(data))
            {
                _logger.LogDebug($"[{portName}] Data is empty, ignoring");
                return;
            }

            buffer.Append(data);
            var bufferContent = buffer.ToString();

            // 来电自动挂断：先处理来电提示，避免无关数据长期堆积在缓冲区
            TryHandleIncomingCall(serialPort, portName, buffer, ref bufferContent);

            _logger.LogInformation($"🔥🔥🔥 [{portName}] 缓冲区内容:");
            _logger.LogInformation($"  缓冲区长度: {bufferContent.Length} 字符");
            _logger.LogInformation($"  缓冲区内容: [{bufferContent}]");

            // 检查 +CMTI: 短信存储通知（与参考脚本一致）
            // 格式: +CMTI: \"SM\",<index>
            if (bufferContent.Contains("+CMTI:"))
            {
                _logger.LogInformation($"[{portName}] +CMTI: detected, processing SMS storage notification...");
                ProcessCmtiNotification(serialPort, bufferContent, portName, buffer);
            }
            // 检查 +CMT: 短信直接推送（某些模块支持）
            // 格式: +CMT: \"发件人号码\",\"\",\"接收时间\"\\r\\n短信内容\\r\\n
            else if (bufferContent.Contains("+CMT:"))
            {
                _logger.LogInformation($"[{portName}] +CMT: detected, processing SMS notification...");
                ProcessCmtNotification(bufferContent, portName, buffer);
            }
            else
            {
                _logger.LogDebug($"[{portName}] No +CMTI: or +CMT: found in buffer yet");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing received data on {portName}");
        }
    }

    /// <summary>
    /// 处理 +CMTI: 短信存储通知（与参考脚本一致）
    /// </summary>
    private async void ProcessCmtiNotification(SerialPort serialPort, string data, string portName, StringBuilder buffer)
    {
        try
        {
            // 匹配 +CMTI: "SM",<index>
            var cmtiPattern = @"\+CMTI:\s*""([^""]+)"",\s*(\d+)";
            var match = Regex.Match(data, cmtiPattern);

            if (match.Success)
            {
                var memory = match.Groups[1].Value;
                var index = match.Groups[2].Value;

                _logger.LogInformation($"📨 [{portName}] 收到短信提示: 存储={memory} 索引={index}");

                // 读取短信内容
                try
                {
                    // 先尝试读取指定索引的短信
                    var response = SendAtCommandAsync(serialPort, $"AT+CMGR={index}", CancellationToken.None).Result;
                    
                    _logger.LogInformation($"📨 [{portName}] AT+CMGR={index} 原始响应:");
                    _logger.LogInformation($"  {response}");
                    
                    // 检查响应是否只有 OK（说明短信已被读取或删除）
                    if (string.IsNullOrWhiteSpace(response) || response.Trim() == "OK")
                    {
                        _logger.LogWarning($"[{portName}] AT+CMGR={index} 返回空内容，尝试列出所有短信...");
                        
                        // 尝试列出所有短信
                        response = SendAtCommandAsync(serialPort, "AT+CMGL=\"ALL\"", CancellationToken.None).Result;
                        _logger.LogInformation($"📨 [{portName}] AT+CMGL=\"ALL\" 响应:");
                        _logger.LogInformation($"  {response}");
                        
                        // 如果还是没有，尝试只列出未读短信
                        if (string.IsNullOrWhiteSpace(response) || response.Trim() == "OK")
                        {
                            response = SendAtCommandAsync(serialPort, "AT+CMGL=\"REC UNREAD\"", CancellationToken.None).Result;
                            _logger.LogInformation($"📨 [{portName}] AT+CMGL=\"REC UNREAD\" 响应:");
                            _logger.LogInformation($"  {response}");
                        }
                    }
                    
                    // 解析响应格式:
                    // +CMGR: "REC UNREAD","+8613800138000",,"26/01/23,00:04:45+32"
                    // 短信内容在这里
                    // OK
                    // 或者 +CMGL 格式:
                    // +CMGL: 1,"REC UNREAD","+8613800138000",,"26/01/23,00:04:45+32"
                    // 短信内容
                    var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    
                    string senderNumber = "";
                    string timestamp = "";
                    string messageContent = "";
                    bool foundHeader = false;
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        
                        // 跳过空行和 OK
                        if (string.IsNullOrWhiteSpace(line) || line == "OK")
                        {
                            continue;
                        }
                        
                        // 解析 +CMGR: 或 +CMGL: 头部
                        if (line.StartsWith("+CMGR:") || line.StartsWith("+CMGL:"))
                        {
                            foundHeader = true;
                            
                            // 匹配格式: +CMGR: "状态","发件人号码",,"时间戳"
                            // 或: +CMGL: 索引,"状态","发件人号码",,"时间戳"
                            var cmgrPattern = @"\+CM[GR][LR]:\s*(?:\d+,)?""[^""]*"",""([^""]+)"",""[^""]*"",""([^""]+)""";
                            var cmgrMatch = Regex.Match(line, cmgrPattern);
                            
                            if (cmgrMatch.Success)
                            {
                                senderNumber = cmgrMatch.Groups[1].Value;
                                timestamp = cmgrMatch.Groups[2].Value;
                            }
                            else
                            {
                                // 尝试简化的匹配（某些模块可能省略部分字段）
                                var simplePattern = @"""(\+?\d+)""";
                                var simpleMatches = Regex.Matches(line, simplePattern);
                                if (simpleMatches.Count >= 2)
                                {
                                    senderNumber = simpleMatches[1].Value.Trim('"');
                                    if (simpleMatches.Count >= 4)
                                    {
                                        timestamp = simpleMatches[3].Value.Trim('"');
                                    }
                                }
                            }
                        }
                        // +CMGR:/+CMGL: 头部之后的行就是短信内容
                        else if (foundHeader)
                        {
                            if (!string.IsNullOrEmpty(messageContent))
                            {
                                messageContent += "\n";
                            }
                            messageContent += line;
                        }
                    }
                    
                    // 打印解析后的短信信息
                    if (foundHeader && !string.IsNullOrEmpty(messageContent))
                    {
                        var decodedMessageContent = DecodeUcs2IfNeeded(messageContent, portName);
                        var receivedTime = string.IsNullOrEmpty(timestamp) ? DateTime.Now : ParseSmsTimestamp(timestamp);
                        
                        _logger.LogInformation("📨 ========== 收到新短信 ==========");
                        _logger.LogInformation($"📡 接收端口: {portName}");
                        _logger.LogInformation($"📞 来信号码: {senderNumber}");
                        _logger.LogInformation($"🕐 接收时间: {receivedTime:yyyy-MM-dd HH:mm:ss}");
                        _logger.LogInformation($"📝 短信内容: {decodedMessageContent}");
                        _logger.LogInformation("=====================================");
                        
                        // 触发短信接收事件
                        try
                        {
                            var deviceId = _configuration["SignalR:DeviceId"] ?? Environment.MachineName;
                            OnSmsReceived?.Invoke(new Margin.Models.SmsReceivedDto
                            {
                                DeviceId = deviceId,
                                ComPort = portName,
                                SenderNumber = senderNumber,
                                MessageContent = decodedMessageContent,
                                ReceivedTime = receivedTime,
                                SmsTimestamp = timestamp
                            });
                        }
                        catch (Exception eventEx)
                        {
                            _logger.LogError(eventEx, $"[{portName}] 触发短信接收事件失败");
                        }

                        // 读取成功后删除短信，避免重复读取
                        try
                        {
                            await SendAtCommandAsync(serialPort, $"AT+CMGD={index}", CancellationToken.None);
                            _logger.LogDebug($"[{portName}] 已删除短信索引 {index}");
                        }
                        catch (Exception delEx)
                        {
                            _logger.LogWarning(delEx, $"[{portName}] 删除短信索引 {index} 失败");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"[{portName}] 无法解析短信响应或短信内容为空");
                        _logger.LogWarning($"[{portName}] 完整响应: {response}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to read SMS at index {index}");
                }

                // 清除已处理的部分
                var processedIndex = match.Index + match.Length;
                if (processedIndex < buffer.Length)
                {
                    buffer.Remove(0, processedIndex);
                }
                else
                {
                    buffer.Clear();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing +CMTI notification on {portName}");
        }
    }

    /// <summary>
    /// 处理 +CMT: 短信直接推送
    /// </summary>
    private void ProcessCmtNotification(string data, string portName, StringBuilder buffer)
    {
        try
        {
            _logger.LogInformation($"📨 [{portName}] ProcessCmtNotification 原始数据:");
            _logger.LogInformation($"  数据长度: {data.Length} 字符");
            _logger.LogInformation($"  原始内容: {data}");
            
            // 匹配 +CMT: 格式的短信
            // 实际格式: +CMT: "+61494546223",,"26/01/23,03:46:06+40"
            //           321231
            // 注意：中间是两个逗号 ,, 而不是 "",""
            var cmtPattern = @"\+CMT:\s*""([^""]+)"",\s*,\s*""([^""]+)""\r?\n\r?\n([\s\S]+?)(?=\r?\n\r?\n|\r?\n\+CMT:|\r?\nOK|$)";
            var matches = Regex.Matches(data, cmtPattern, RegexOptions.Singleline);

            _logger.LogInformation($"📨 [{portName}] 正则匹配结果: 找到 {matches.Count} 个匹配");

            if (matches.Count == 0)
            {
                // 尝试更宽松的匹配：短信内容可能还没完全接收
                var headerPattern = @"\+CMT:\s*""([^""]+)"",\s*,\s*""([^""]+)""";
                var headerMatch = Regex.Match(data, headerPattern);
                
                if (headerMatch.Success)
                {
                    _logger.LogInformation($"📨 [{portName}] 找到 +CMT: 头部，但短信内容可能还未完全接收");
                    _logger.LogInformation($"  发件人: {headerMatch.Groups[1].Value}");
                    _logger.LogInformation($"  时间戳: {headerMatch.Groups[2].Value}");
                    _logger.LogInformation($"  等待更多数据...");
                    return; // 等待更多数据
                }
                else
                {
                    _logger.LogWarning($"[{portName}] 无法匹配 +CMT: 格式");
                    _logger.LogWarning($"[{portName}] 数据内容: {data}");
                    return;
                }
            }

            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count >= 4)
                {
                    var senderNumber = match.Groups[1].Value.Trim();
                    var timestamp = match.Groups[2].Value.Trim();
                    var messageContent = match.Groups[3].Value.Trim();

                    _logger.LogInformation($"📨 [{portName}] 成功解析短信:");
                    _logger.LogInformation($"  发件人: {senderNumber}");
                    _logger.LogInformation($"  时间戳: {timestamp}");
                    _logger.LogInformation($"  原始内容: {messageContent}");

                    // 🔧 UCS2解码：检查是否为十六进制编码的UCS2内容
                    messageContent = DecodeUcs2IfNeeded(messageContent, portName);

                    _logger.LogInformation($"  解码后内容: {messageContent}");

                    // 解析时间戳 (格式: YY/MM/DD,HH:MM:SS+TZ)
                    var receivedTime = ParseSmsTimestamp(timestamp);

                    // 打印短信信息
                    _logger.LogInformation("📨 ========== 收到新短信 ==========");
                    _logger.LogInformation($"📡 接收端口: {portName}");
                    _logger.LogInformation($"📞 来信号码: {senderNumber}");
                    _logger.LogInformation($"🕐 接收时间: {receivedTime:yyyy-MM-dd HH:mm:ss}");
                    _logger.LogInformation($"📝 短信内容: {messageContent}");
                    _logger.LogInformation("=====================================");

                    // 触发短信接收事件
                    try
                    {
                        var deviceId = _configuration["SignalR:DeviceId"] ?? Environment.MachineName;
                        OnSmsReceived?.Invoke(new Margin.Models.SmsReceivedDto
                        {
                            DeviceId = deviceId,
                            ComPort = portName,
                            SenderNumber = senderNumber,
                            MessageContent = messageContent,
                            ReceivedTime = receivedTime,
                            SmsTimestamp = timestamp
                        });
                    }
                    catch (Exception eventEx)
                    {
                        _logger.LogError(eventEx, $"[{portName}] 触发短信接收事件失败");
                    }

                    // 清除已处理的部分
                    var processedIndex = match.Index + match.Length;
                    if (processedIndex < buffer.Length)
                    {
                        buffer.Remove(0, processedIndex);
                    }
                    else
                    {
                        buffer.Clear();
                    }
                }
            }

            // 如果缓冲区太大，清空避免内存泄漏
            if (buffer.Length > 10000)
            {
                _logger.LogWarning($"Buffer too large on {portName}, clearing...");
                buffer.Clear();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing SMS notification on {portName}");
        }
    }

    /// <summary>
    /// 解析短信时间戳
    /// </summary>
    private DateTime ParseSmsTimestamp(string timestamp)
    {
        try
        {
            // 格式: YY/MM/DD,HH:MM:SS+TZ
            // 示例: 26/01/22,14:30:45+32
            var pattern = @"(\d{2})/(\d{2})/(\d{2}),(\d{2}):(\d{2}):(\d{2})([+-]\d{2})";
            var match = Regex.Match(timestamp, pattern);

            if (match.Success)
            {
                var year = 2000 + int.Parse(match.Groups[1].Value);
                var month = int.Parse(match.Groups[2].Value);
                var day = int.Parse(match.Groups[3].Value);
                var hour = int.Parse(match.Groups[4].Value);
                var minute = int.Parse(match.Groups[5].Value);
                var second = int.Parse(match.Groups[6].Value);

                // ✅ 指定 DateTimeKind.Utc 以兼容 PostgreSQL timestamp with time zone
                return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to parse SMS timestamp: {timestamp}");
        }

        // ✅ DateTime.Now 也需要转换为 UTC
        return DateTime.UtcNow;
    }

    /// <summary>
    /// UCS2解码：如果内容是十六进制编码的UCS2，则解码为文本
    /// </summary>
    private string DecodeUcs2IfNeeded(string content, string portName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            // 仅保留十六进制主体，兼容首尾空白/引号等噪声。
            var cleanContent = content.Replace(" ", "").Replace("\r", "").Replace("\n", "").Trim('"');

            if (!Regex.IsMatch(cleanContent, "^[0-9A-Fa-f]+$") || cleanContent.Length < 4)
            {
                _logger.LogDebug($"[{portName}] 内容不是UCS2十六进制，直接使用原文本");
                return content;
            }

            // 串口偶发脏尾巴时，尽量裁剪到可解码边界，避免整条短信回退为原始hex。
            if (cleanContent.Length % 2 != 0)
            {
                _logger.LogWarning($"[{portName}] UCS2内容出现半字节尾部，已丢弃最后1个字符后继续解码");
                cleanContent = cleanContent[..^1];
            }

            if ((cleanContent.Length / 2) % 2 != 0)
            {
                _logger.LogWarning($"[{portName}] UCS2内容出现单字节尾部，已丢弃最后2个字符后继续解码");
                cleanContent = cleanContent[..^2];
            }

            if (cleanContent.Length < 4)
            {
                return content;
            }

            _logger.LogInformation($"🔧 [{portName}] 检测到UCS2编码内容，开始解码...");
            var bytes = Convert.FromHexString(cleanContent);
            var decoded = Encoding.BigEndianUnicode.GetString(bytes);

            _logger.LogInformation($"✅ [{portName}] UCS2解码成功: {decoded}");
            return decoded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[{portName}] UCS2解码失败，返回原内容");
            return content;
        }
    }

    /// <summary>
    /// 暂停指定端口的监听（用于发送短信时临时释放串口）
    /// </summary>
    public bool PauseListening(string portName)
    {
        if (_portListeners.TryGetValue(portName, out var listener))
        {
            try
            {
                _logger.LogInformation($"🔧 [DEBUG] 暂停端口 {portName} 的监听");
                
                if (listener.SerialPort != null && listener.SerialPort.IsOpen)
                {
                    listener.SerialPort.Close();
                    _logger.LogInformation($"✅ [DEBUG] 端口 {portName} 已关闭");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"暂停端口 {portName} 监听失败");
            }
        }
        
        return false;
    }

    /// <summary>
    /// 恢复指定端口的监听（发送短信完成后重新打开串口）
    /// </summary>
    public async Task<bool> ResumeListeningAsync(string portName, CancellationToken cancellationToken = default)
    {
        if (_portListeners.TryGetValue(portName, out var listener))
        {
            try
            {
                _logger.LogInformation($"🔧 [DEBUG] 恢复端口 {portName} 的监听");
                
                if (listener.SerialPort != null && !listener.SerialPort.IsOpen)
                {
                    listener.SerialPort.Open();
                    _logger.LogInformation($"✅ [DEBUG] 端口 {portName} 已重新打开");
                    
                    // 等待端口稳定
                    await Task.Delay(500, cancellationToken);
                    
                    // 重新初始化短信接收设置
                    await InitializeSmsSettingsAsync(listener.SerialPort, portName, cancellationToken);
                    
                    _logger.LogInformation($"✅ [DEBUG] 端口 {portName} 监听已恢复");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"恢复端口 {portName} 监听失败");
            }
        }
        
        return false;
    }

    /// <summary>
    /// 停止监听（异步入口，便于统一调用）
    /// </summary>
    public Task StopListeningAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止监听
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        
        foreach (var kvp in _portListeners)
        {
            try
            {
                var listener = kvp.Value;
                listener.CancellationTokenSource.Cancel();
                
                if (listener.SerialPort != null && listener.SerialPort.IsOpen)
                {
                    listener.SerialPort.Close();
                    listener.SerialPort.Dispose();
                }
                
                _logger.LogInformation($"SMS receiver stopped on {kvp.Key}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error stopping SMS receiver on {kvp.Key}");
            }
        }
        
        _portListeners.Clear();
    }

    public void Dispose()
    {
        Stop();
    }
}
