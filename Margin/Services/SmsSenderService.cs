using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace Margin.Services;

/// <summary>
/// 短信发送服务 - 支持通过指定COM口发送短信
/// </summary>
public class SmsSenderService : IDisposable
{
    private readonly ILogger<SmsSenderService> _logger;
    private readonly Dictionary<string, SerialPort> _serialPorts = new();
    private readonly object _lock = new();
    private readonly SmsReceiverService _receiverService;

    public SmsSenderService(ILogger<SmsSenderService> logger, SmsReceiverService receiverService)
    {
        _logger = logger;
        _receiverService = receiverService;
    }

    /// <summary>
    /// 发送短信
    /// </summary>
    /// <param name="comPort">COM口名称（如 COM3）</param>
    /// <param name="targetNumber">目标号码</param>
    /// <param name="messageContent">短信内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>发送结果（成功返回true，失败返回false并包含错误信息）</returns>
    public async Task<(bool Success, string? ErrorMessage)> SendSmsAsync(
        string comPort, 
        string targetNumber, 
        string messageContent, 
        CancellationToken cancellationToken = default)
    {
        SerialPort? serialPort = null;
        bool listenerPaused = false;

        try
        {
            _logger.LogInformation($"📤 准备发送短信: COM={comPort}, 目标={targetNumber}, 内容长度={messageContent.Length}");

            // 验证参数
            if (string.IsNullOrWhiteSpace(comPort))
            {
                return (false, "COM口不能为空");
            }

            if (string.IsNullOrWhiteSpace(targetNumber))
            {
                return (false, "目标号码不能为空");
            }

            if (string.IsNullOrWhiteSpace(messageContent))
            {
                return (false, "短信内容不能为空");
            }

            // 🔧 关键修复：发送前暂停监听服务，释放串口
            _logger.LogInformation($"🔧 [DEBUG] 暂停 {comPort} 的监听服务...");
            listenerPaused = _receiverService.PauseListening(comPort);
            
            if (listenerPaused)
            {
                _logger.LogInformation($"✅ [DEBUG] 监听服务已暂停，等待串口释放...");
                await Task.Delay(1000, cancellationToken); // 等待串口完全释放
            }

            // 获取或创建串口连接
            bool needsInitialization = false;
            lock (_lock)
            {
                if (_serialPorts.TryGetValue(comPort, out var existingPort) && existingPort.IsOpen)
                {
                    serialPort = existingPort;
                    _logger.LogDebug($"使用已存在的串口连接: {comPort}");
                }
                else
                {
                    // 创建新的串口连接
                    serialPort = new SerialPort(comPort)
                    {
                        BaudRate = 115200,
                        DataBits = 8,
                        StopBits = StopBits.One,
                        Parity = Parity.None,
                        ReadTimeout = 5000,
                        WriteTimeout = 5000,
                        DtrEnable = true,
                        RtsEnable = true,
                        Encoding = Encoding.ASCII,
                        NewLine = "\r\n"  // 明确设置换行符为 CRLF
                    };

                    serialPort.Open();
                    _serialPorts[comPort] = serialPort;
                    needsInitialization = true;
                    _logger.LogInformation($"✅ 串口已打开: {comPort}");
                }
            }

            // 等待端口稳定（在lock外部）
            if (needsInitialization)
            {
                await Task.Delay(500, cancellationToken);
            }

            // 初始化短信设置
            await InitializeSmsSettingsAsync(serialPort, cancellationToken);

            // 发送短信
            var sendResult = await SendSmsCommandAsync(serialPort, targetNumber, messageContent, cancellationToken);

            if (sendResult.Success)
            {
                _logger.LogInformation($"✅ 短信发送成功: {comPort} -> {targetNumber}");
                return (true, null);
            }
            else
            {
                _logger.LogWarning($"❌ 短信发送失败: {comPort} -> {targetNumber}, 错误: {sendResult.ErrorMessage}");
                return (false, sendResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"发送短信时发生异常: {comPort} -> {targetNumber}");
            return (false, $"发送异常: {ex.Message}");
        }
        finally
        {
            // 🔧 关键修复：发送完成后恢复监听服务
            if (listenerPaused)
            {
                _logger.LogInformation($"🔧 [DEBUG] 恢复 {comPort} 的监听服务...");
                await _receiverService.ResumeListeningAsync(comPort, cancellationToken);
            }
            
            // 注意：不关闭串口，保持连接以便复用
            // 串口会在 Dispose 时统一关闭
        }
    }

    /// <summary>
    /// 初始化短信发送设置
    /// </summary>
    private async Task InitializeSmsSettingsAsync(SerialPort serialPort, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🔧 [DEBUG] 开始初始化短信设置...");
            
            // 先发送一个简单的AT命令测试通信
            _logger.LogInformation("🔧 [DEBUG] 测试串口通信 - 发送 AT");
            var testResponse = await SendAtCommandAsync(serialPort, "AT", cancellationToken);
            _logger.LogInformation($"🔧 [DEBUG] AT 测试响应: [{testResponse}]");
            
            if (string.IsNullOrEmpty(testResponse) || !testResponse.Contains("OK"))
            {
                _logger.LogWarning($"⚠️ AT 测试命令未收到正确响应，可能串口通信有问题");
            }
            
            await Task.Delay(300, cancellationToken);
            
            // 关闭回显
            _logger.LogInformation("🔧 [DEBUG] 发送 ATE0 关闭回显");
            var ate0Response = await SendAtCommandAsync(serialPort, "ATE0", cancellationToken);
            _logger.LogInformation($"🔧 [DEBUG] ATE0 响应: [{ate0Response}]");
            await Task.Delay(300, cancellationToken);

            // 设置短信格式为文本模式
            _logger.LogInformation("🔧 [DEBUG] 发送 AT+CMGF=1 设置文本模式");
            var cmgfResponse = await SendAtCommandAsync(serialPort, "AT+CMGF=1", cancellationToken);
            _logger.LogInformation($"🔧 [DEBUG] AT+CMGF=1 响应: [{cmgfResponse}]");
            await Task.Delay(300, cancellationToken);

            // 设置字符集为 UCS2 以支持中文
            _logger.LogInformation("🔧 [DEBUG] 发送 AT+CSCS=\"UCS2\" 设置字符集为UCS2（支持中文）");
            var cscsResponse = await SendAtCommandAsync(serialPort, "AT+CSCS=\"UCS2\"", cancellationToken);
            _logger.LogInformation($"🔧 [DEBUG] AT+CSCS 响应: [{cscsResponse}]");
            await Task.Delay(300, cancellationToken);

            _logger.LogInformation("✅ [DEBUG] 短信发送设置初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 初始化短信发送设置失败");
            throw; // 重新抛出异常，让调用者知道初始化失败
        }
    }

    /// <summary>
    /// 发送短信AT命令
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> SendSmsCommandAsync(
        SerialPort serialPort, 
        string targetNumber, 
        string messageContent, 
        CancellationToken cancellationToken)
    {
        try
        {
            // 清空缓冲区中的旧数据
            _logger.LogInformation("🔧 [DEBUG] 清空串口缓冲区");
            try
            {
                if (serialPort.BytesToRead > 0)
                {
                    var oldData = serialPort.ReadExisting();
                    _logger.LogInformation($"🔧 [DEBUG] 清空了 {oldData.Length} 字节的旧数据: [{oldData}]");
                }
                else
                {
                    _logger.LogInformation("🔧 [DEBUG] 缓冲区为空，无需清空");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"🔧 [DEBUG] 清空缓冲区时出错: {ex.Message}");
            }
            
            await Task.Delay(200, cancellationToken);
            
            // 步骤1: 发送 AT+CMGS 命令,指定目标号码
            var cmgsCommand = $"AT+CMGS=\"{targetNumber}\"";
            _logger.LogInformation($"🔧 [DEBUG] 准备发送命令: [{cmgsCommand}]");
            
            // 记录发送前的串口状态
            _logger.LogInformation($"🔧 [DEBUG] 串口状态 - IsOpen: {serialPort.IsOpen}, BaudRate: {serialPort.BaudRate}, BytesToRead: {serialPort.BytesToRead}");
            
            serialPort.WriteLine(cmgsCommand);
            _logger.LogInformation($"🔧 [DEBUG] 命令已写入串口: [{cmgsCommand}]");
            
            // 等待命令发送完成
            await Task.Delay(200, cancellationToken);
            
            // 等待 ">" 提示符(表示可以输入短信内容)
            var promptReceived = false;
            var startTime = Environment.TickCount;
            var timeout = 10000; // 增加到10秒超时
            var responseBuffer = new StringBuilder();
            
            _logger.LogInformation("🔧 [DEBUG] 开始等待 '>' 提示符...");
            
            while (Environment.TickCount - startTime < timeout)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return (false, "操作已取消");
                }
                
                try
                {
                    var bytesToRead = serialPort.BytesToRead;
                    if (bytesToRead > 0)
                    {
                        var response = serialPort.ReadExisting();
                        responseBuffer.Append(response);
                        _logger.LogInformation($"🔧 [DEBUG] 收到 {bytesToRead} 字节响应: [{response}] (十六进制: {BitConverter.ToString(Encoding.ASCII.GetBytes(response))})");
                        
                        var fullResponse = responseBuffer.ToString();
                        
                        if (fullResponse.Contains(">"))
                        {
                            _logger.LogInformation($"🔧 [DEBUG] ✅ 检测到 '>' 提示符！完整响应: [{fullResponse}]");
                            promptReceived = true;
                            break;
                        }
                        
                        if (fullResponse.Contains("ERROR") || fullResponse.Contains("+CMS ERROR"))
                        {
                            _logger.LogError($"🔧 [DEBUG] ❌ AT+CMGS命令返回错误: [{fullResponse}]");
                            return (false, $"AT+CMGS命令失败: {fullResponse}");
                        }
                    }
                }
                catch (TimeoutException)
                {
                    _logger.LogDebug("🔧 [DEBUG] ReadExisting 超时，继续等待");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"🔧 [DEBUG] 读取响应时出错: {ex.Message}");
                }
                
                await Task.Delay(100, cancellationToken);
            }
            
            if (!promptReceived)
            {
                return (false, "未收到短信输入提示符 '>'");
            }
            
            // 步骤2: 发送短信内容，以 Ctrl+Z (0x1A) 结束
            _logger.LogInformation($"🔧 [DEBUG] 发送短信内容: {messageContent}");
            
            // 直接发送文本内容，GSM模块会根据 AT+CSCS="UCS2" 设置自动处理编码
            serialPort.Write(messageContent);
            serialPort.Write(new byte[] { 0x1A }, 0, 1); // Ctrl+Z
            
            _logger.LogInformation($"🔧 [DEBUG] 短信内容已发送，等待确认...");
            
            // 步骤3: 等待发送结果
            startTime = Environment.TickCount;
            timeout = 30000; // 30秒超时（发送短信可能需要较长时间）
            
            responseBuffer.Clear(); // 清空之前的响应缓冲区
            
            while (Environment.TickCount - startTime < timeout)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return (false, "操作已取消");
                }
                
                try
                {
                    if (serialPort.BytesToRead > 0)
                    {
                        var response = serialPort.ReadExisting();
                        responseBuffer.Append(response);
                        _logger.LogDebug($"收到响应: {response}");
                        
                        var fullResponse = responseBuffer.ToString();
                        
                        // 检查是否发送成功（返回 +CMGS: <mr> 和 OK）
                        if (fullResponse.Contains("+CMGS:") && fullResponse.Contains("OK"))
                        {
                            // 提取消息引用号
                            var match = Regex.Match(fullResponse, @"\+CMGS:\s*(\d+)");
                            var messageRef = match.Success ? match.Groups[1].Value : "unknown";
                            _logger.LogInformation($"短信发送成功，消息引用号: {messageRef}");
                            return (true, null);
                        }
                        
                        // 检查是否发送失败
                        if (fullResponse.Contains("ERROR") || fullResponse.Contains("+CMS ERROR"))
                        {
                            return (false, $"短信发送失败: {fullResponse}");
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // 继续等待
                }
                
                await Task.Delay(100, cancellationToken);
            }
            
            return (false, "发送超时，未收到确认响应");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送短信命令时发生异常");
            return (false, $"发送异常: {ex.Message}");
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

        _logger.LogDebug($"发送AT命令: {command}");
        
        // 清空缓冲区
        var clearStart = Environment.TickCount;
        while (Environment.TickCount - clearStart < 200)
        {
            try
            {
                if (serialPort.BytesToRead > 0)
                {
                    serialPort.ReadExisting();
                }
            }
            catch
            {
                await Task.Delay(20, cancellationToken);
            }
        }
        
        // 发送命令
        serialPort.WriteLine(command);
        
        // 读取响应
        var lines = new List<string>();
        var startTime = Environment.TickCount;
        var timeout = 5000;
        
        while (Environment.TickCount - startTime < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            
            try
            {
                if (serialPort.BytesToRead > 0)
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
                    _logger.LogDebug($"AT响应: {line}");
                    
                    // 检查终止标记
                    if (line == "OK" || 
                        line == "ERROR" || 
                        line.StartsWith("+CME ERROR") || 
                        line.StartsWith("+CMS ERROR"))
                    {
                        return string.Join("\r\n", lines);
                    }
                }
            }
            catch (TimeoutException)
            {
                await Task.Delay(30, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"读取AT响应时出错: {ex.Message}");
                await Task.Delay(30, cancellationToken);
            }
        }
        
        return string.Join("\r\n", lines);
    }

    /// <summary>
    /// 关闭指定COM口的连接
    /// </summary>
    public void ClosePort(string comPort)
    {
        lock (_lock)
        {
            if (_serialPorts.TryGetValue(comPort, out var serialPort))
            {
                try
                {
                    if (serialPort.IsOpen)
                    {
                        serialPort.Close();
                    }
                    serialPort.Dispose();
                    _serialPorts.Remove(comPort);
                    _logger.LogInformation($"串口已关闭: {comPort}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"关闭串口失败: {comPort}");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var kvp in _serialPorts)
            {
                try
                {
                    if (kvp.Value.IsOpen)
                    {
                        kvp.Value.Close();
                    }
                    kvp.Value.Dispose();
                    _logger.LogInformation($"串口已关闭: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"关闭串口失败: {kvp.Key}");
                }
            }
            _serialPorts.Clear();
        }
    }
}
