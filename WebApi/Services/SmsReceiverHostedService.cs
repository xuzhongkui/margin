using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WebApi.Contracts.DeviceCom;
using WebApi.Data;
using WebApi.Hubs;
using WebApi.Models;

namespace WebApi.Services;

/// <summary>
/// 后台服务：监听SignalR短信事件并保存到数据库
/// </summary>
public class SmsReceiverHostedService : IHostedService
{
    private readonly ILogger<SmsReceiverHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<DeviceHub> _hubContext;

    public SmsReceiverHostedService(
        ILogger<SmsReceiverHostedService> logger,
        IServiceProvider serviceProvider,
        IHubContext<DeviceHub> hubContext)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("📨 SMS Receiver Hosted Service started");
        
        // 注意：SignalR Hub方法是由客户端调用的，不需要在这里订阅
        // 我们需要修改DeviceHub来直接保存短信到数据库
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("📨 SMS Receiver Hosted Service stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存短信到数据库（供Hub调用）
    /// </summary>
    public static async Task SaveSmsToDatabase(
        string deviceId, 
        string smsDataJson, 
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        try
        {
            // 反序列化短信数据
            var smsDto = JsonSerializer.Deserialize<SmsReceivedDto>(smsDataJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (smsDto == null)
            {
                logger.LogWarning("Failed to deserialize SMS data");
                return;
            }

            // 创建新的scope来获取DbContext
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SmsManageDbContext>();

            // 从 DeviceComSnapshot 查询运营商信息
            string? operatorName = null;
            try
            {
                var snapshot = await dbContext.DeviceComSnapshots
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.DeviceId == smsDto.DeviceId);

                if (snapshot != null)
                {
                    var ports = JsonSerializer.Deserialize<List<DeviceComPortDto>>(snapshot.DataJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var matchedPort = ports?.FirstOrDefault(p => p.PortName == smsDto.ComPort);
                    operatorName = matchedPort?.ModemInfo?.Operator;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"Failed to query operator info for {smsDto.DeviceId}/{smsDto.ComPort}");
            }

            // 创建短信记录
            var smsMessage = new SmsMessage
            {
                DeviceId = smsDto.DeviceId,
                ComPort = smsDto.ComPort,
                SenderNumber = smsDto.SenderNumber,
                MessageContent = smsDto.MessageContent,
                ReceivedTime = smsDto.ReceivedTime,
                SmsTimestamp = smsDto.SmsTimestamp,
                Operator = operatorName
            };

            dbContext.SmsMessages.Add(smsMessage);
            await dbContext.SaveChangesAsync();

            logger.LogInformation($"✅ SMS saved to database: {smsDto.SenderNumber} -> {smsDto.ComPort} (Operator: {operatorName ?? "N/A"})");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save SMS to database");
        }
    }

    /// <summary>
    /// 保存挂断记录到数据库（供Hub调用）
    /// </summary>
    public static async Task SaveHangupToDatabase(
        string deviceId,
        string hangupDataJson,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<CallHangupDto>(hangupDataJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto == null)
            {
                logger.LogWarning("Failed to deserialize hangup data");
                return;
            }

            if (string.IsNullOrWhiteSpace(dto.ComPort))
            {
                logger.LogWarning("Hangup data ComPort is empty, ignored");
                return;
            }

            // Create new scope to get DbContext. Avoid sharing DbContext across hub invocations.
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SmsManageDbContext>();

            var record = new CallHangupRecord
            {
                DeviceId = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId,
                ComPort = dto.ComPort,
                CallerNumber = dto.CallerNumber,
                HangupTime = dto.HangupTimeUtc,
                Reason = dto.Reason,
                RawLine = dto.RawLine,
                CreateTime = DateTime.UtcNow,
                UpdateTime = DateTime.UtcNow,
                IsDelete = false
            };

            dbContext.CallHangupRecords.Add(record);
            await dbContext.SaveChangesAsync();

            logger.LogInformation($"✅ Hangup record saved: {record.DeviceId}/{record.ComPort} caller={record.CallerNumber ?? ""} time={record.HangupTime:O}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save hangup record to database");
        }
    }
}

/// <summary>
/// 短信接收数据传输对象（与Margin端保持一致）
/// </summary>
public class SmsReceivedDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string ComPort { get; set; } = string.Empty;
    public string SenderNumber { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
    public DateTime ReceivedTime { get; set; }
    public string? SmsTimestamp { get; set; }
}
