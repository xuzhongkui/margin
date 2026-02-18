using System.Text.Json;
using Margin.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace Margin.Services;

/// <summary>
/// COM 口配置（与 WebAPI 的 ComPortConfig 保持一致）
/// </summary>
public class ComPortConfig
{
    public string PortName { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 115200;
}

/// <summary>
/// SignalR client service for communicating with the server
/// </summary>
public class SignalRService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonSerializerOptionsCamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<SignalRService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ComPortScanner _comPortScanner;
    private HubConnection? _hubConnection;
    private string _deviceId = string.Empty;

    private readonly SmsReceiverService _smsReceiverService;
    private readonly SmsSenderService _smsSenderService;
    private bool _smsReceiverEventHooked;
    private bool _smsReceiverStarted;

    public SignalRService(
        ILogger<SignalRService> logger, 
        IConfiguration configuration,
        ComPortScanner comPortScanner,
        SmsReceiverService smsReceiverService,
        SmsSenderService smsSenderService)
    {
        _logger = logger;
        _configuration = configuration;
        _comPortScanner = comPortScanner;
        _smsReceiverService = smsReceiverService;
        _smsSenderService = smsSenderService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var serverUrl = _configuration["SignalR:ServerUrl"] ?? "https://localhost:7001";
        _deviceId = _configuration["SignalR:DeviceId"] ?? Environment.MachineName;

        _logger.LogInformation($"🔧 [SignalR] Initializing connection...");
        _logger.LogInformation($"🔧 [SignalR] Server URL: {serverUrl}/hubs/device");
        _logger.LogInformation($"🔧 [SignalR] Device ID: {_deviceId}");

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/hubs/device")
            .WithAutomaticReconnect()
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();

        // Register event handlers BEFORE connecting
        _logger.LogInformation($"🔧 [SignalR] Registering event handler for 'ScanComPorts'...");
        
        _hubConnection.On<string>("ScanComPorts", async (targetDeviceId) =>
        {
            _logger.LogInformation($"📨 [SignalR] ===== MESSAGE RECEIVED =====");
            _logger.LogInformation($"📨 [SignalR] Event: ScanComPorts");
            _logger.LogInformation($"📨 [SignalR] Target Device: {targetDeviceId}");
            _logger.LogInformation($"📨 [SignalR] My Device ID: {_deviceId}");
            _logger.LogInformation($"📨 [SignalR] Match: {targetDeviceId == _deviceId}");
            
            // Check if this request is for this device
            if (targetDeviceId == _deviceId || string.IsNullOrEmpty(targetDeviceId))
            {
                _logger.LogInformation($"✅ [SignalR] Scan request ACCEPTED for device: {_deviceId}");
                
                // Send immediate acknowledgment
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    _logger.LogInformation($"📤 [SignalR] Sending acknowledgment...");
                    await _hubConnection.InvokeAsync("SendScanAcknowledgment", _deviceId, "Scan request received, starting scan...");
                    _logger.LogInformation($"✅ [SignalR] Acknowledgment sent");
                }
                
                await HandleScanRequest();
            }
            else
            {
                _logger.LogInformation($"❌ [SignalR] Scan request IGNORED. Not for this device (target: {targetDeviceId}, mine: {_deviceId})");
            }
        });

        // 注册短信监听启动事件
        _hubConnection.On<string, List<ComPortConfig>>("StartSmsReceiver", async (targetDeviceId, ports) =>
        {
            _logger.LogInformation($"📨 [SignalR] ===== StartSmsReceiver MESSAGE RECEIVED =====");
            _logger.LogInformation($"📨 [SignalR] Target Device: {targetDeviceId}");
            _logger.LogInformation($"📨 [SignalR] My Device ID: {_deviceId}");
            _logger.LogInformation($"📨 [SignalR] Ports: {string.Join(", ", ports.Select(p => $"{p.PortName}@{p.BaudRate}"))}");
            
            if (targetDeviceId == _deviceId || string.IsNullOrEmpty(targetDeviceId))
            {
                _logger.LogInformation($"✅ [SignalR] StartSmsReceiver request ACCEPTED");
                await HandleStartSmsReceiverRequest(ports);
            }
            else
            {
                _logger.LogInformation($"❌ [SignalR] StartSmsReceiver request IGNORED. Not for this device");
            }
        });

        // 注册短信监听停止事件
        _hubConnection.On<string>("StopSmsReceiver", async (targetDeviceId) =>
        {
            _logger.LogInformation($"📨 [SignalR] ===== StopSmsReceiver MESSAGE RECEIVED =====");
            _logger.LogInformation($"📨 [SignalR] Target Device: {targetDeviceId}");
            _logger.LogInformation($"📨 [SignalR] My Device ID: {_deviceId}");
            
            if (targetDeviceId == _deviceId || string.IsNullOrEmpty(targetDeviceId))
            {
                _logger.LogInformation($"✅ [SignalR] StopSmsReceiver request ACCEPTED");
                await HandleStopSmsReceiverRequest();
            }
            else
            {
                _logger.LogInformation($"❌ [SignalR] StopSmsReceiver request IGNORED. Not for this device");
            }
        });

        // 注册短信发送事件
        _hubConnection.On<SmsSendRequest>("SendSms", async (request) =>
        {
            _logger.LogInformation($"📨 [SignalR] ===== SendSms MESSAGE RECEIVED =====");
            _logger.LogInformation($"📨 [SignalR] Target Device: {request.DeviceId}");
            _logger.LogInformation($"📨 [SignalR] My Device ID: {_deviceId}");
            _logger.LogInformation($"📨 [SignalR] COM: {request.ComPort}, Target: {request.TargetNumber}, RecordId: {request.RecordId}");
            
            if (request.DeviceId == _deviceId || string.IsNullOrEmpty(request.DeviceId))
            {
                _logger.LogInformation($"✅ [SignalR] SendSms request ACCEPTED");
                await HandleSendSmsRequest(request);
            }
            else
            {
                _logger.LogInformation($"❌ [SignalR] SendSms request IGNORED. Not for this device");
            }
        });
        
        _logger.LogInformation($"✅ [SignalR] Event handler registered successfully");

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning($"Connection lost. Reconnecting... Error: {error?.Message}");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += connectionId =>
        {
            _logger.LogInformation($"Reconnected. Connection ID: {connectionId}");
            return RegisterDeviceAsync();
        };

        _hubConnection.Closed += error =>
        {
            _logger.LogError($"Connection closed. Error: {error?.Message}");
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync(cancellationToken);
            _logger.LogInformation("Connected to SignalR hub");

            // Register this device
            await RegisterDeviceAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR hub");
            throw;
        }
    }

    private async Task RegisterDeviceAsync()
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _hubConnection.InvokeAsync("RegisterDevice", _deviceId);
                _logger.LogInformation($"Device registered: {_deviceId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register device");
            }
        }
    }

    private async Task HandleScanRequest()
    {
        try
        {
            _logger.LogInformation("Starting COM port scan...");

            // Push each port immediately to server to avoid long wait.
            var scanResult = await _comPortScanner.ScanComPortsAsync(portInfo =>
            {
                if (_hubConnection?.State != HubConnectionState.Connected)
                {
                    return;
                }

                try
                {
                    var jsonPort = JsonSerializer.Serialize(portInfo, JsonSerializerOptionsCamelCase);
                    _hubConnection.InvokeAsync("SendComPortFound", _deviceId, jsonPort)
                        .ContinueWith(t =>
                        {
                            if (t.Exception != null)
                            {
                                _logger.LogWarning($"Failed to send ComPortFound: {t.Exception.GetBaseException().Message}");
                            }
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to serialize/send ComPortFound: {ex.Message}");
                }
            });

            await StartSmsReceiverForSimPortsAsync(scanResult);

            var jsonResult = JsonSerializer.Serialize(scanResult, JsonSerializerOptionsCamelCase);

            // 🔍 调试：打印序列化后的JSON
            _logger.LogInformation("📊 Serialized scan result:");
            _logger.LogInformation(jsonResult);

            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                await _hubConnection.InvokeAsync("SendComPortScanResult", _deviceId, jsonResult);
                await _hubConnection.InvokeAsync("SendComPortScanCompleted", _deviceId, scanResult.ScanTime.ToString("O"));
                _logger.LogInformation("✅ Scan result sent to server");
            }
            else
            {
                _logger.LogWarning("Cannot send scan result - not connected to hub");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling scan request");
        }
    }

    private async Task StartSmsReceiverForSimPortsAsync(ComPortScanResult scanResult)
    {
        if (_smsReceiverStarted)
        {
            _logger.LogInformation("📱 SMS receiver already started, skipping auto-start");
            return;
        }

        var autoStart = _configuration.GetValue("SmsReceiver:AutoStartOnScan", true);
        if (!autoStart)
        {
            _logger.LogInformation("📱 Auto-start disabled by configuration (SmsReceiver:AutoStartOnScan=false)");
            return;
        }

        var ports = scanResult.AvailablePorts
            .Where(p => p.IsSmsModem)
            .Where(p => p.ModemInfo?.HasSimCard == true)
            .Where(p => p.BaudRate.HasValue)
            .Select(p => new ComPortConfig { PortName = p.PortName, BaudRate = p.BaudRate!.Value })
            .ToList();

        if (ports.Count == 0)
        {
            _logger.LogInformation("📱 No SIM-ready modems found, auto-start skipped");
            return;
        }

        _logger.LogInformation($"📱 Auto-starting SMS receiver for {ports.Count} SIM-ready port(s)...");
        await HandleStartSmsReceiverRequest(ports);
        _smsReceiverStarted = true;
    }

    public async Task StopAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            _logger.LogInformation("Disconnected from SignalR hub");
        }
    }

    private async Task HandleStartSmsReceiverRequest(List<ComPortConfig> ports)
    {
        try
        {
            _logger.LogInformation($"📱 Starting SMS receiver for {ports.Count} port(s)...");

            var portConfigs = ports.Select(p => (p.PortName, p.BaudRate)).ToList();
            
            if (!_smsReceiverEventHooked)
            {
                // 注册短信/挂断上报事件处理器
                _smsReceiverService.OnSmsReceived += async (smsDto) =>
                {
                    try
                    {
                        _logger.LogInformation($"📤 [SignalR] 准备推送短信到服务器: {smsDto.SenderNumber} -> {smsDto.ComPort}");
                        
                        if (_hubConnection?.State == HubConnectionState.Connected)
                        {
                            var jsonSms = JsonSerializer.Serialize(smsDto, JsonSerializerOptionsCamelCase);
                            await _hubConnection.InvokeAsync("SendSmsReceived", _deviceId, jsonSms);
                            _logger.LogInformation($"✅ [SignalR] 短信推送成功");
                        }
                        else
                        {
                            _logger.LogWarning($"⚠️ [SignalR] 无法推送短信 - SignalR未连接");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send SMS to server via SignalR");
                    }
                };

                _smsReceiverService.OnCallHangup += async (hangupDto) =>
                {
                    try
                    {
                        _logger.LogInformation($"📤 [SignalR] 准备上报挂断记录到服务器: {hangupDto.CallerNumber ?? ""} -> {hangupDto.ComPort}");

                        if (_hubConnection?.State == HubConnectionState.Connected)
                        {
                            var jsonHangup = JsonSerializer.Serialize(hangupDto, JsonSerializerOptionsCamelCase);
                            await _hubConnection.InvokeAsync("SendCallHangupRecord", _deviceId, jsonHangup);
                            _logger.LogInformation($"✅ [SignalR] 挂断记录上报成功");
                        }
                        else
                        {
                            _logger.LogWarning($"⚠️ [SignalR] 无法上报挂断记录 - SignalR未连接");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send hangup record to server via SignalR");
                    }
                };

                _smsReceiverEventHooked = true;
            }

            // 启动短信监听（使用 CancellationToken.None，因为这是长期运行的任务）
            _ = Task.Run(async () =>
            {
                try
                {
                    await _smsReceiverService.StartListeningAsync(portConfigs, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in SMS receiver background task");
                }
            });

            _logger.LogInformation($"✅ SMS receiver started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting SMS receiver");
        }
    }

    private async Task HandleStopSmsReceiverRequest()
    {
        try
        {
            _logger.LogInformation($"📱 Stopping SMS receiver...");
            
            _smsReceiverService.Stop();
            
            _logger.LogInformation($"✅ SMS receiver stopped successfully");
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping SMS receiver");
        }
    }

    private async Task HandleSendSmsRequest(SmsSendRequest request)
    {
        try
        {
            _logger.LogInformation($"📤 Sending SMS: COM={request.ComPort}, Target={request.TargetNumber}, RecordId={request.RecordId}");
            
            // 调用短信发送服务
            var (success, errorMessage) = await _smsSenderService.SendSmsAsync(
                request.ComPort,
                request.TargetNumber,
                request.MessageContent,
                CancellationToken.None
            );
            
            // 发送结果回服务器
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                var status = success ? "Success" : "Failed";
                await _hubConnection.InvokeAsync("SendSmsResult", request.RecordId, status, errorMessage);
                _logger.LogInformation($"✅ SMS send result reported: {status}");
            }
            else
            {
                _logger.LogWarning($"⚠️ Cannot report SMS send result - SignalR not connected");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling SMS send request");
            
            // 尝试报告失败
            try
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.InvokeAsync("SendSmsResult", request.RecordId, "Failed", ex.Message);
                }
            }
            catch (Exception reportEx)
            {
                _logger.LogError(reportEx, "Failed to report SMS send error");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}

/// <summary>
/// 短信发送请求（与服务器端保持一致）
/// </summary>
public class SmsSendRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string ComPort { get; set; } = string.Empty;
    public string TargetNumber { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
}


