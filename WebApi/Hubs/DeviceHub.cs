using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using WebApi.Services;

namespace WebApi.Hubs;

/// <summary>
/// SignalR Hub for device communication
/// </summary>
public class DeviceHub : Hub
{
    // 单实例可用；多实例需要分布式存储（如 Redis）来维护在线设备列表。
    private static readonly ConcurrentDictionary<string, string> _connectedDevices = new();

    public static List<string> GetConnectedDeviceIdsSnapshot()
    {
        // Hub 自身用静态字典维护连接状态；Controller 侧通过该快照 API 获取当前列表。
        var snapshot = _connectedDevices.Values.ToArray();
        return snapshot
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToList();
    }

    /// <summary>
    /// Device registers itself with a unique identifier
    /// </summary>
    public async Task RegisterDevice(string deviceId)
    {
        _connectedDevices[Context.ConnectionId] = deviceId;
        await Clients.Others.SendAsync("DeviceConnected", deviceId);
        Console.WriteLine($"Device registered: {deviceId} (ConnectionId: {Context.ConnectionId})");
    }

    /// <summary>
    /// Device sends acknowledgment that scan request was received
    /// </summary>
    public async Task SendScanAcknowledgment(string deviceId, string message)
    {
        Console.WriteLine($"✅ Scan acknowledgment from {deviceId}: {message}");
        await Clients.All.SendAsync("ScanAcknowledgment", deviceId, message);
    }

    /// <summary>
    /// Device sends COM port scan results back to server
    /// </summary>
    public async Task SendComPortScanResult(string deviceId, string comPortData)
    {
        Console.WriteLine($"Received COM port scan from {deviceId}: {comPortData}");
        await Clients.All.SendAsync("ComPortScanResult", deviceId, comPortData);
    }

    /// <summary>
    /// Device sends single COM port info back to server (incremental push)
    /// </summary>
    public async Task SendComPortFound(string deviceId, string comPortInfo)
    {
        await Clients.All.SendAsync("ComPortFound", deviceId, comPortInfo);
    }

    /// <summary>
    /// Device notifies scan completed
    /// </summary>
    public async Task SendComPortScanCompleted(string deviceId, string scanTimeIso)
    {
        await Clients.All.SendAsync("ComPortScanCompleted", deviceId, scanTimeIso);
    }

    /// <summary>
    /// Server requests device to scan COM ports
    /// </summary>
    public async Task RequestComPortScan(string deviceId)
    {
        Console.WriteLine($"📤 [Hub] RequestComPortScan called for device: {deviceId}");
        
        var connectionId = _connectedDevices.FirstOrDefault(x => x.Value == deviceId).Key;
        if (!string.IsNullOrEmpty(connectionId))
        {
            Console.WriteLine($"📤 [Hub] Found connection ID: {connectionId}");
            Console.WriteLine($"📤 [Hub] Sending ScanComPorts event with deviceId: {deviceId}");
            
            // ✅ 修复：传递deviceId参数
            await Clients.Client(connectionId).SendAsync("ScanComPorts", deviceId);
            
            Console.WriteLine($"✅ [Hub] Scan request sent to device: {deviceId}");
        }
        else
        {
            Console.WriteLine($"❌ [Hub] Device not found: {deviceId}");
            Console.WriteLine($"📋 [Hub] Connected devices: {string.Join(", ", _connectedDevices.Values)}");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectedDevices.TryRemove(Context.ConnectionId, out var deviceId))
        {
            await Clients.Others.SendAsync("DeviceDisconnected", deviceId);
            Console.WriteLine($"Device disconnected: {deviceId}");
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Get all connected devices
    /// </summary>
    public List<string> GetConnectedDevices()
    {
        return _connectedDevices.Values.ToList();
    }

    /// <summary>
    /// Device sends received SMS to server
    /// </summary>
    public async Task SendSmsReceived(string deviceId, string smsDataJson)
    {
        Console.WriteLine($"📨 [Hub] Received SMS from device {deviceId}: {smsDataJson}");

        // 保存短信到数据库
        var serviceProvider = Context.GetHttpContext()?.RequestServices;
        if (serviceProvider != null)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DeviceHub>>();
            await SmsReceiverHostedService.SaveSmsToDatabase(deviceId, smsDataJson, serviceProvider, logger);
        }

        // 广播给所有客户端
        await Clients.All.SendAsync("SmsReceived", deviceId, smsDataJson);
    }

    /// <summary>
    /// Device reports an auto-hangup event to server
    /// </summary>
    public async Task SendCallHangupRecord(string deviceId, string hangupDataJson)
    {
        Console.WriteLine($"📵 [Hub] Received hangup record from device {deviceId}: {hangupDataJson}");

        // 保存挂断记录到数据库
        var serviceProvider = Context.GetHttpContext()?.RequestServices;
        if (serviceProvider != null)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DeviceHub>>();
            await SmsReceiverHostedService.SaveHangupToDatabase(deviceId, hangupDataJson, serviceProvider, logger);
        }

        // 广播给所有客户端
        await Clients.All.SendAsync("CallHangupRecord", deviceId, hangupDataJson);
    }

    /// <summary>
    /// Server requests device to send SMS
    /// </summary>
    public async Task RequestSendSms(string deviceId, string comPort, string targetNumber, string messageContent, string recordId)
    {
        Console.WriteLine($"📤 [Hub] RequestSendSms called for device: {deviceId}, COM: {comPort}, Target: {targetNumber}");
        
        var connectionId = _connectedDevices.FirstOrDefault(x => x.Value == deviceId).Key;
        if (!string.IsNullOrEmpty(connectionId))
        {
            Console.WriteLine($"📤 [Hub] Found connection ID: {connectionId}");
            Console.WriteLine($"📤 [Hub] Sending SendSms event to device");
            
            // 发送短信请求给指定设备
            await Clients.Client(connectionId).SendAsync("SendSms", new
            {
                deviceId,
                comPort,
                targetNumber,
                messageContent,
                recordId
            });
            
            Console.WriteLine($"✅ [Hub] SMS send request sent to device: {deviceId}");
        }
        else
        {
            Console.WriteLine($"❌ [Hub] Device not found: {deviceId}");
            Console.WriteLine($"📋 [Hub] Connected devices: {string.Join(", ", _connectedDevices.Values)}");
        }
    }

    /// <summary>
    /// Device sends SMS send result back to server
    /// </summary>
    public async Task SendSmsResult(string recordId, string status, string? errorMessage)
    {
        Console.WriteLine($"📨 [Hub] Received SMS send result: RecordId={recordId}, Status={status}, Error={errorMessage}");
        
        // 更新发送记录状态
        var serviceProvider = Context.GetHttpContext()?.RequestServices;
        if (serviceProvider != null)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Data.SmsManageDbContext>();
                var logger = serviceProvider.GetRequiredService<ILogger<DeviceHub>>();
                
                var record = await dbContext.SmsSendRecords.FindAsync(Guid.Parse(recordId));
                if (record != null)
                {
                    record.Status = status;
                    record.ErrorMessage = errorMessage;
                    if (status == "Success" || status == "Failed")
                    {
                        record.SentTime = DateTime.UtcNow;
                    }
                    await dbContext.SaveChangesAsync();
                    logger.LogInformation($"✅ SMS send record updated: {recordId} -> {status}");
                }
                else
                {
                    logger.LogWarning($"⚠️ SMS send record not found: {recordId}");
                }
            }
            catch (Exception ex)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<DeviceHub>>();
                logger.LogError(ex, $"Failed to update SMS send record: {recordId}");
            }
        }
        
        // 广播给所有客户端
        await Clients.All.SendAsync("SmsSendResult", recordId, status, errorMessage);
    }
}
