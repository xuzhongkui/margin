using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Controllers;

[ApiController]
[Route("api/call-hangup-records")]
[Authorize]
public sealed class CallHangupRecordsController : ControllerBase
{
    private readonly SmsManageDbContext _dbContext;

    public CallHangupRecordsController(SmsManageDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取挂断记录列表（分页）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? deviceId = null,
        [FromQuery] string? comPort = null,
        [FromQuery] string? callerNumber = null,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1)
        {
            pageNumber = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 20;
        }

        if (pageSize > 200)
        {
            pageSize = 200;
        }

        // 普通用户：限制只能看自己被分配到的设备/COM
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "用户未登录" });
        }

        var user = await _dbContext.Users.FindAsync(new object[] { Guid.Parse(userId) }, cancellationToken);
        if (user is null)
        {
            return Unauthorized(new { message = "用户不存在" });
        }

        var query = _dbContext.CallHangupRecords.AsQueryable();
        if (includeDeleted)
        {
            query = query.IgnoreQueryFilters();
        }

        query = query.AsNoTracking();

        if (user.Role == UserRole.User)
        {
            var allocations = await _dbContext.UserComAllocations
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .ToListAsync(cancellationToken);

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

            var allowedDeviceIds = allocations
                .Select(x => x.DeviceId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var allowedComPorts = new List<string>();
            foreach (var allocation in allocations)
            {
                try
                {
                    var comList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(allocation.ComListJson);
                    if (comList != null)
                    {
                        allowedComPorts.AddRange(comList);
                    }
                }
                catch
                {
                    // ignore
                }
            }

            allowedComPorts = allowedComPorts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!allowedDeviceIds.Any() || !allowedComPorts.Any())
            {
                return Ok(new
                {
                    totalCount = 0,
                    pageNumber,
                    pageSize,
                    data = Array.Empty<object>()
                });
            }

            var allowedDeviceIdsUpper = allowedDeviceIds
                .Select(x => x.ToUpper())
                .ToList();
            var allowedComPortsUpper = allowedComPorts
                .Select(x => x.ToUpper())
                .ToList();

            query = query.Where(x =>
                allowedDeviceIdsUpper.Contains(x.DeviceId.Trim().ToUpper())
                && allowedComPortsUpper.Contains(x.ComPort.Trim().ToUpper()));
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

        if (!string.IsNullOrWhiteSpace(callerNumber))
        {
            query = query.Where(x => x.CallerNumber != null && x.CallerNumber.Contains(callerNumber));
        }

        if (startTime.HasValue)
        {
            query = query.Where(x => x.HangupTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(x => x.HangupTime <= endTime.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var readIds = await _dbContext.MessageReadReceipts
            .AsNoTracking()
            .Where(x => x.UserId == user.Id && x.MessageType == MessageTypes.Hangup)
            .Select(x => x.SourceId)
            .ToListAsync(cancellationToken);

        var readSet = readIds.Count == 0 ? null : readIds.ToHashSet();

        var records = await query
            .OrderByDescending(x => x.HangupTime)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.DeviceId,
                x.ComPort,
                x.CallerNumber,
                x.HangupTime,
                x.Reason,
                x.RawLine,
                x.IsDelete,
                x.CreateTime,
                x.UpdateTime,
                x.Remark,
                isRead = readSet != null && readSet.Contains(x.Id)
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            totalCount,
            pageNumber,
            pageSize,
            data = records
        });
    }

    /// <summary>
    /// 管理员硬删除挂断记录（物理删除）
    /// </summary>
    [HttpDelete("admin/hard-delete/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> HardDeleteCallHangupRecord(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.CallHangupRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (record is null)
        {
            return NotFound(new { message = "来电记录不存在" });
        }

        _dbContext.CallHangupRecords.Remove(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "来电记录已永久删除" });
    }

    /// <summary>
    /// 用户删除挂断记录（软删除）
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCallHangupRecord(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.CallHangupRecords
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (record is null)
        {
            return NotFound(new { message = "来电记录不存在" });
        }

        record.IsDelete = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "来电记录删除成功" });
    }
}
