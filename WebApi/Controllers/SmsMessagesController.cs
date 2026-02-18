using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SmsMessagesController : ControllerBase
{
    private readonly SmsManageDbContext _context;
    private readonly ILogger<SmsMessagesController> _logger;

    public SmsMessagesController(
        SmsManageDbContext context,
        ILogger<SmsMessagesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// 获取短信列表（分页）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSmsMessages(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? deviceId = null,
        [FromQuery] string? comPort = null,
        [FromQuery] string? senderNumber = null,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "用户未登录" });
        }

        var user = await _context.Users.FindAsync(Guid.Parse(userId));
        if (user == null)
        {
            return Unauthorized(new { message = "用户不存在" });
        }

        var query = _context.SmsMessages.AsNoTracking().AsQueryable();

        // 普通用户软删除后不再显示
        if (user.Role == UserRole.User)
        {
            query = query.Where(x => !x.IsDelete);

            var allocations = await _context.UserComAllocations
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .ToListAsync();

            if (!allocations.Any())
            {
                return Ok(new
                {
                    totalCount = 0,
                    pageNumber,
                    pageSize,
                    data = Array.Empty<object>()
                });
            }

            // 解析所有分配的COM口
            var allocatedComPorts = new List<string>();
            foreach (var allocation in allocations)
            {
                try
                {
                    var comList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(allocation.ComListJson);
                    if (comList != null)
                    {
                        allocatedComPorts.AddRange(comList);
                    }
                }
                catch
                {
                    // 忽略JSON解析错误
                }
            }

            allocatedComPorts = allocatedComPorts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!allocatedComPorts.Any())
            {
                return Ok(new
                {
                    totalCount = 0,
                    pageNumber,
                    pageSize,
                    data = Array.Empty<object>()
                });
            }

            var allocatedComPortsUpper = allocatedComPorts
                .Select(x => x.ToUpper())
                .ToList();

            query = query.Where(x => allocatedComPortsUpper.Contains(x.ComPort.Trim().ToUpper()));
        }

        var normalizedDeviceId = deviceId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedDeviceId))
        {
            var deviceIdUpper = normalizedDeviceId.ToUpper();
            query = query.Where(x => x.DeviceId.Trim().ToUpper() == deviceIdUpper);
        }

        var normalizedComPort = comPort?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedComPort))
        {
            var comPortUpper = normalizedComPort.ToUpper();
            query = query.Where(x => x.ComPort.Trim().ToUpper() == comPortUpper);
        }

        if (!string.IsNullOrWhiteSpace(senderNumber))
        {
            query = query.Where(x => x.SenderNumber.Contains(senderNumber));
        }

        if (startTime.HasValue)
        {
            query = query.Where(x => x.ReceivedTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(x => x.ReceivedTime <= endTime.Value);
        }

        var totalCount = await query.CountAsync();

        var readIds = await _context.MessageReadReceipts
            .AsNoTracking()
            .Where(x => x.UserId == user.Id && x.MessageType == MessageTypes.Sms)
            .Select(x => x.SourceId)
            .ToListAsync();

        var readSet = readIds.Count == 0 ? null : readIds.ToHashSet();

        var messages = await query
            .OrderByDescending(x => x.ReceivedTime)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.DeviceId,
                x.ComPort,
                x.Operator,
                x.SenderNumber,
                x.MessageContent,
                x.ReceivedTime,
                x.SmsTimestamp,
                x.IsDelete,
                x.CreateTime,
                x.UpdateTime,
                x.Remark,
                isRead = readSet != null && readSet.Contains(x.Id)
            })
            .ToListAsync();

        return Ok(new
        {
            totalCount,
            pageNumber,
            pageSize,
            data = messages
        });
    }

    /// <summary>
    /// 管理员查看所有短信（包括软删除的）
    /// </summary>
    [HttpGet("admin/all")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAllSmsMessagesForAdmin(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? deviceId = null,
        [FromQuery] string? comPort = null,
        [FromQuery] string? senderNumber = null,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] bool? includeDeleted = true)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "用户未登录" });
        }

        var user = await _context.Users.FindAsync(Guid.Parse(userId));
        if (user == null)
        {
            return Unauthorized(new { message = "用户不存在" });
        }

        // 使用 IgnoreQueryFilters 忽略全局软删除过滤器
        var query = _context.SmsMessages.IgnoreQueryFilters().AsNoTracking().AsQueryable();

        // 如果不包含已删除的，则过滤掉
        if (includeDeleted == false)
        {
            query = query.Where(x => !x.IsDelete);
        }

        var normalizedDeviceId = deviceId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedDeviceId))
        {
            var deviceIdUpper = normalizedDeviceId.ToUpper();
            query = query.Where(x => x.DeviceId.Trim().ToUpper() == deviceIdUpper);
        }

        var normalizedComPort = comPort?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedComPort))
        {
            var comPortUpper = normalizedComPort.ToUpper();
            query = query.Where(x => x.ComPort.Trim().ToUpper() == comPortUpper);
        }

        if (!string.IsNullOrWhiteSpace(senderNumber))
        {
            query = query.Where(x => x.SenderNumber.Contains(senderNumber));
        }

        if (startTime.HasValue)
        {
            query = query.Where(x => x.ReceivedTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(x => x.ReceivedTime <= endTime.Value);
        }

        var totalCount = await query.CountAsync();

        var readIds = await _context.MessageReadReceipts
            .AsNoTracking()
            .Where(x => x.UserId == user.Id && x.MessageType == MessageTypes.Sms)
            .Select(x => x.SourceId)
            .ToListAsync();

        var readSet = readIds.Count == 0 ? null : readIds.ToHashSet();

        var messages = await query
            .OrderByDescending(x => x.ReceivedTime)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.DeviceId,
                x.ComPort,
                x.Operator,
                x.SenderNumber,
                x.MessageContent,
                x.ReceivedTime,
                x.SmsTimestamp,
                x.IsDelete,
                x.CreateTime,
                x.UpdateTime,
                x.Remark,
                isRead = readSet != null && readSet.Contains(x.Id)
            })
            .ToListAsync();

        return Ok(new
        {
            totalCount,
            pageNumber,
            pageSize,
            data = messages
        });
    }

    /// <summary>
    /// 管理员硬删除短信（物理删除）
    /// </summary>
    [HttpDelete("admin/hard-delete/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> HardDeleteSmsMessage(Guid id)
    {
        var message = await _context.SmsMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (message == null)
        {
            return NotFound(new { message = "短信不存在" });
        }

        _context.SmsMessages.Remove(message);
        await _context.SaveChangesAsync();

        return Ok(new { message = "短信已永久删除" });
    }

    /// <summary>
    /// 获取单条短信详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSmsMessage(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var sms = await _context.SmsMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (sms == null)
            {
                return NotFound(new { message = "SMS message not found" });
            }

            return Ok(sms);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get SMS message");
            return StatusCode(500, new { message = "Failed to get SMS message" });
        }
    }

    /// <summary>
    /// 删除短信（软删除）
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSmsMessage(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var sms = await _context.SmsMessages
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (sms == null)
            {
                return NotFound(new { message = "SMS message not found" });
            }

            sms.IsDelete = true;
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "SMS message deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete SMS message");
            return StatusCode(500, new { message = "Failed to delete SMS message" });
        }
    }

    /// <summary>
    /// 批量删除短信（软删除）
    /// </summary>
    [HttpPost("batch-delete")]
    public async Task<IActionResult> BatchDeleteSmsMessages(
        [FromBody] List<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (ids == null || ids.Count == 0)
            {
                return BadRequest(new { message = "No IDs provided" });
            }

            var smsMessages = await _context.SmsMessages
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken);

            foreach (var sms in smsMessages)
            {
                sms.IsDelete = true;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { message = $"{smsMessages.Count} SMS messages deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch delete SMS messages");
            return StatusCode(500, new { message = "Failed to batch delete SMS messages" });
        }
    }

    /// <summary>
    /// 获取短信统计信息
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _context.SmsMessages.AsNoTracking();

            if (startTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime <= endTime.Value);
            }

            var total = await query.CountAsync(cancellationToken);

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

            var bySender = await query
                .GroupBy(x => x.SenderNumber)
                .Select(g => new { senderNumber = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(10)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                total,
                byDevice,
                byComPort,
                topSenders = bySender
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get SMS statistics");
            return StatusCode(500, new { message = "Failed to get SMS statistics" });
        }
    }
}
