using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class SmsSendController : ControllerBase
{
    private readonly SmsManageDbContext _context;
    private readonly ILogger<SmsSendController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<Hubs.DeviceHub> _hubContext;

    public SmsSendController(
        SmsManageDbContext context,
        ILogger<SmsSendController> logger,
        IConfiguration configuration,
        IHubContext<Hubs.DeviceHub> hubContext)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _hubContext = hubContext;
    }

    /// <summary>
    /// 发送短信（通过SignalR通知边缘设备发送）
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> SendSms([FromBody] SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // 验证参数
            if (string.IsNullOrWhiteSpace(request.DeviceId))
            {
                return BadRequest(new { message = "设备ID不能为空" });
            }

            if (string.IsNullOrWhiteSpace(request.ComPort))
            {
                return BadRequest(new { message = "COM口不能为空" });
            }

            if (string.IsNullOrWhiteSpace(request.TargetNumber))
            {
                return BadRequest(new { message = "目标号码不能为空" });
            }

            if (string.IsNullOrWhiteSpace(request.MessageContent))
            {
                return BadRequest(new { message = "短信内容不能为空" });
            }

            // 创建发送记录
            var sendRecord = new SmsSendRecord
            {
                DeviceId = request.DeviceId,
                ComPort = request.ComPort,
                TargetNumber = request.TargetNumber,
                MessageContent = request.MessageContent,
                Status = "Pending",
                TriggerSource = "API",
                TriggerApiUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}"
            };

            _context.SmsSendRecords.Add(sendRecord);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation($"📤 短信发送请求已创建: ID={sendRecord.Id}, Device={request.DeviceId}, COM={request.ComPort}, Target={request.TargetNumber}");

            // 通过SignalR通知边缘设备发送短信
            try
            {
                var connectedDevices = Hubs.DeviceHub.GetConnectedDeviceIdsSnapshot();
                
                if (connectedDevices.Contains(request.DeviceId))
                {
                    await _hubContext.Clients.All.SendAsync("SendSms", new
                    {
                        deviceId = request.DeviceId,
                        comPort = request.ComPort,
                        targetNumber = request.TargetNumber,
                        messageContent = request.MessageContent,
                        recordId = sendRecord.Id.ToString()
                    });
                    _logger.LogInformation($"✅ SignalR通知已发送到设备: {request.DeviceId}");
                }
                else
                {
                    _logger.LogWarning($"⚠️ 设备未连接: {request.DeviceId}");
                }
            }
            catch (Exception signalREx)
            {
                _logger.LogWarning(signalREx, $"SignalR通知发送失败，但记录已创建: {sendRecord.Id}");
            }

            return Ok(new
            {
                message = "短信发送请求已创建并通知边缘设备",
                recordId = sendRecord.Id,
                status = sendRecord.Status
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建短信发送请求失败");
            return StatusCode(500, new { message = "创建短信发送请求失败" });
        }
    }

    /// <summary>
    /// 获取发送记录列表（分页）
    /// </summary>
    [HttpGet("records")]
    public async Task<IActionResult> GetSendRecords(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? deviceId = null,
        [FromQuery] string? comPort = null,
        [FromQuery] string? targetNumber = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _context.SmsSendRecords.AsQueryable();

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                query = query.Where(x => x.DeviceId.Contains(deviceId));
            }

            if (!string.IsNullOrWhiteSpace(comPort))
            {
                query = query.Where(x => x.ComPort.Contains(comPort));
            }

            if (!string.IsNullOrWhiteSpace(targetNumber))
            {
                query = query.Where(x => x.TargetNumber.Contains(targetNumber));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.Status == status);
            }

            if (startTime.HasValue)
            {
                query = query.Where(x => x.CreateTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                query = query.Where(x => x.CreateTime <= endTime.Value);
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var records = await query
                .OrderByDescending(x => x.CreateTime)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                totalCount,
                pageNumber,
                pageSize,
                data = records
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取发送记录失败");
            return StatusCode(500, new { message = "获取发送记录失败" });
        }
    }

    /// <summary>
    /// 获取单条发送记录详情
    /// </summary>
    [HttpGet("records/{id}")]
    public async Task<IActionResult> GetSendRecord(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _context.SmsSendRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (record == null)
            {
                return NotFound(new { message = "发送记录不存在" });
            }

            return Ok(record);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取发送记录详情失败");
            return StatusCode(500, new { message = "获取发送记录详情失败" });
        }
    }

    /// <summary>
    /// 更新发送记录状态（供边缘设备回调）
    /// </summary>
    [HttpPut("records/{id}/status")]
    public async Task<IActionResult> UpdateSendStatus(
        Guid id,
        [FromBody] UpdateStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _context.SmsSendRecords
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (record == null)
            {
                return NotFound(new { message = "发送记录不存在" });
            }

            record.Status = request.Status;
            record.ErrorMessage = request.ErrorMessage;

            if (request.Status == "Success" || request.Status == "Failed")
            {
                record.SentTime = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation($"📤 发送记录状态已更新: ID={id}, Status={request.Status}");

            return Ok(new { message = "状态更新成功" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新发送记录状态失败");
            return StatusCode(500, new { message = "更新发送记录状态失败" });
        }
    }

    /// <summary>
    /// 删除发送记录（软删除）
    /// </summary>
    [HttpDelete("records/{id}")]
    public async Task<IActionResult> DeleteSendRecord(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _context.SmsSendRecords
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (record == null)
            {
                return NotFound(new { message = "发送记录不存在" });
            }

            record.IsDelete = true;
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "发送记录已删除" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除发送记录失败");
            return StatusCode(500, new { message = "删除发送记录失败" });
        }
    }

    /// <summary>
    /// 获取发送统计信息
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _context.SmsSendRecords.AsNoTracking();

            if (startTime.HasValue)
            {
                query = query.Where(x => x.CreateTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                query = query.Where(x => x.CreateTime <= endTime.Value);
            }

            var total = await query.CountAsync(cancellationToken);
            var pending = await query.CountAsync(x => x.Status == "Pending", cancellationToken);
            var sending = await query.CountAsync(x => x.Status == "Sending", cancellationToken);
            var success = await query.CountAsync(x => x.Status == "Success", cancellationToken);
            var failed = await query.CountAsync(x => x.Status == "Failed", cancellationToken);

            var byDevice = await query
                .GroupBy(x => x.DeviceId)
                .Select(g => new { deviceId = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(cancellationToken);

            var byComPort = await query
                .GroupBy(x => x.ComPort)
                .Select(g => new { comPort = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                total,
                pending,
                sending,
                success,
                failed,
                successRate = total > 0 ? Math.Round((double)success / total * 100, 2) : 0,
                byDevice,
                byComPort
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取发送统计信息失败");
            return StatusCode(500, new { message = "获取发送统计信息失败" });
        }
    }
}

/// <summary>
/// 发送短信请求
/// </summary>
public class SendSmsRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string ComPort { get; set; } = string.Empty;
    public string TargetNumber { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
}

/// <summary>
/// 更新状态请求
/// </summary>
public class UpdateStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
