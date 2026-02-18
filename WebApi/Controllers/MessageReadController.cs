using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Controllers;

[ApiController]
[Route("api/message-read")]
[Authorize]
public sealed class MessageReadController : ControllerBase
{
    private readonly SmsManageDbContext _dbContext;

    public MessageReadController(SmsManageDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private Guid? TryGetUserId()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return Guid.TryParse(userId, out var id) ? id : null;
    }

    public sealed class UnreadCountsResponse
    {
        public int Sms { get; set; }
        public int Hangup { get; set; }
    }

    [HttpGet("unread-counts")]
    public async Task<IActionResult> GetUnreadCounts(CancellationToken cancellationToken = default)
    {
        var userId = TryGetUserId();
        if (userId is null)
        {
            return Unauthorized(new { message = "用户未登录" });
        }

        // 基于“已读回执”做差集计数：未读 = 当前用户可见的记录 - 已读回执。
        // 这里复用现有的可见性规则：短信按 UserComAllocation 限制 COM；来电按 UserComAllocation 限制 DeviceId+COM。
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken);

        if (user is null)
        {
            return Unauthorized(new { message = "用户不存在" });
        }

        // 普通用户：限制分配范围；管理员：看全部(不含软删除)
        IQueryable<SmsMessage> smsQuery = _dbContext.SmsMessages.AsNoTracking();
        IQueryable<CallHangupRecord> hangupQuery = _dbContext.CallHangupRecords.AsNoTracking();

        if (user.Role == UserRole.User)
        {
            var allocations = await _dbContext.UserComAllocations
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .ToListAsync(cancellationToken);

            if (!allocations.Any())
            {
                return Ok(new UnreadCountsResponse { Sms = 0, Hangup = 0 });
            }

            // 允许的 DeviceId 集合（来电用）
            var allowedDeviceIdsUpper = allocations
                .Select(x => x.DeviceId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpper())
                .Distinct()
                .ToList();

            // 允许的 COM 集合（短信/来电都用）
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

            var allowedComPortsUpper = allowedComPorts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpper())
                .Distinct()
                .ToList();

            if (!allowedComPortsUpper.Any())
            {
                return Ok(new UnreadCountsResponse { Sms = 0, Hangup = 0 });
            }

            smsQuery = smsQuery.Where(x => allowedComPortsUpper.Contains(x.ComPort.Trim().ToUpper()));

            if (allowedDeviceIdsUpper.Any())
            {
                hangupQuery = hangupQuery.Where(x =>
                    allowedDeviceIdsUpper.Contains(x.DeviceId.Trim().ToUpper())
                    && allowedComPortsUpper.Contains(x.ComPort.Trim().ToUpper()));
            }
            else
            {
                hangupQuery = hangupQuery.Where(_ => false);
            }
        }

        var smsReadIds = _dbContext.MessageReadReceipts
            .AsNoTracking()
            .Where(x => x.UserId == user.Id && x.MessageType == MessageTypes.Sms)
            .Select(x => x.SourceId);

        var hangupReadIds = _dbContext.MessageReadReceipts
            .AsNoTracking()
            .Where(x => x.UserId == user.Id && x.MessageType == MessageTypes.Hangup)
            .Select(x => x.SourceId);

        var smsUnread = await smsQuery
            .Where(x => !smsReadIds.Contains(x.Id))
            .CountAsync(cancellationToken);

        var hangupUnread = await hangupQuery
            .Where(x => !hangupReadIds.Contains(x.Id))
            .CountAsync(cancellationToken);

        return Ok(new UnreadCountsResponse { Sms = smsUnread, Hangup = hangupUnread });
    }

    public sealed class MarkReadRequest
    {
        public string MessageType { get; set; } = string.Empty;
        public Guid SourceId { get; set; }
    }

    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead([FromBody] MarkReadRequest request, CancellationToken cancellationToken = default)
    {
        var userId = TryGetUserId();
        if (userId is null)
        {
            return Unauthorized(new { message = "用户未登录" });
        }

        if (request is null)
        {
            return BadRequest(new { message = "请求体不能为空" });
        }

        var messageType = (request.MessageType ?? string.Empty).Trim();
        if (messageType != MessageTypes.Sms && messageType != MessageTypes.Hangup)
        {
            return BadRequest(new { message = "无效的 MessageType" });
        }

        if (request.SourceId == Guid.Empty)
        {
            return BadRequest(new { message = "SourceId 不能为空" });
        }

        // 仅插入一条回执；重复插入由唯一索引保护。
        var receipt = new MessageReadReceipt
        {
            UserId = userId.Value,
            MessageType = messageType,
            SourceId = request.SourceId,
            ReadTimeUtc = DateTime.UtcNow,
            IsDelete = false
        };

        try
        {
            _dbContext.MessageReadReceipts.Add(receipt);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 可能是重复已读（唯一索引冲突），视为成功。
        }

        return Ok(new { message = "已读" });
    }

    public sealed class MarkAllReadRequest
    {
        public string MessageType { get; set; } = string.Empty;

        // 可选：限制到当前筛选范围（与前端filters一致）
        public string? DeviceId { get; set; }
        public string? ComPort { get; set; }
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead([FromBody] MarkAllReadRequest request, CancellationToken cancellationToken = default)
    {
        var userId = TryGetUserId();
        if (userId is null)
        {
            return Unauthorized(new { message = "用户未登录" });
        }

        if (request is null)
        {
            return BadRequest(new { message = "请求体不能为空" });
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken);

        if (user is null)
        {
            return Unauthorized(new { message = "用户不存在" });
        }

        var messageType = (request.MessageType ?? string.Empty).Trim();
        if (messageType != MessageTypes.Sms && messageType != MessageTypes.Hangup)
        {
            return BadRequest(new { message = "无效的 MessageType" });
        }

        var normalizedDeviceId = request.DeviceId?.Trim();
        var normalizedComPort = request.ComPort?.Trim();

        if (messageType == MessageTypes.Sms)
        {
            IQueryable<SmsMessage> query = _dbContext.SmsMessages.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(normalizedDeviceId))
            {
                var deviceUpper = normalizedDeviceId.ToUpper();
                query = query.Where(x => x.DeviceId.Trim().ToUpper() == deviceUpper);
            }

            if (!string.IsNullOrWhiteSpace(normalizedComPort))
            {
                var comUpper = normalizedComPort.ToUpper();
                query = query.Where(x => x.ComPort.Trim().ToUpper() == comUpper);
            }

            if (user.Role == UserRole.User)
            {
                var allocations = await _dbContext.UserComAllocations
                    .AsNoTracking()
                    .Where(x => x.UserId == user.Id)
                    .ToListAsync(cancellationToken);

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

                var allowedComPortsUpper = allowedComPorts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpper())
                    .Distinct()
                    .ToList();

                if (!allowedComPortsUpper.Any())
                {
                    return Ok(new { message = "ok" });
                }

                query = query.Where(x => allowedComPortsUpper.Contains(x.ComPort.Trim().ToUpper()));
            }

            var sourceIds = await query
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            await BulkInsertReceipts(user.Id, MessageTypes.Sms, sourceIds, cancellationToken);
            return Ok(new { message = "ok" });
        }

        // Hangup
        IQueryable<CallHangupRecord> hangup = _dbContext.CallHangupRecords.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(normalizedDeviceId))
        {
            var deviceUpper = normalizedDeviceId.ToUpper();
            hangup = hangup.Where(x => x.DeviceId.Trim().ToUpper() == deviceUpper);
        }

        if (!string.IsNullOrWhiteSpace(normalizedComPort))
        {
            var comUpper = normalizedComPort.ToUpper();
            hangup = hangup.Where(x => x.ComPort.Trim().ToUpper() == comUpper);
        }

        if (user.Role == UserRole.User)
        {
            var allocations = await _dbContext.UserComAllocations
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .ToListAsync(cancellationToken);

            var allowedDeviceIdsUpper = allocations
                .Select(x => x.DeviceId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpper())
                .Distinct()
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

            var allowedComPortsUpper = allowedComPorts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpper())
                .Distinct()
                .ToList();

            if (!allowedDeviceIdsUpper.Any() || !allowedComPortsUpper.Any())
            {
                return Ok(new { message = "ok" });
            }

            hangup = hangup.Where(x =>
                allowedDeviceIdsUpper.Contains(x.DeviceId.Trim().ToUpper())
                && allowedComPortsUpper.Contains(x.ComPort.Trim().ToUpper()));
        }

        var hangupIds = await hangup
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        await BulkInsertReceipts(user.Id, MessageTypes.Hangup, hangupIds, cancellationToken);
        return Ok(new { message = "ok" });
    }

    private async Task BulkInsertReceipts(Guid userId, string messageType, List<Guid> sourceIds, CancellationToken cancellationToken)
    {
        if (sourceIds.Count == 0)
        {
            return;
        }

        // 避免重复：先取出已存在的 SourceId
        var existing = await _dbContext.MessageReadReceipts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.MessageType == messageType && sourceIds.Contains(x.SourceId))
            .Select(x => x.SourceId)
            .ToListAsync(cancellationToken);

        var existingSet = existing.Count == 0 ? null : existing.ToHashSet();

        var now = DateTime.UtcNow;
        var toInsert = new List<MessageReadReceipt>(capacity: sourceIds.Count);
        foreach (var id in sourceIds)
        {
            if (existingSet != null && existingSet.Contains(id))
            {
                continue;
            }

            toInsert.Add(new MessageReadReceipt
            {
                UserId = userId,
                MessageType = messageType,
                SourceId = id,
                ReadTimeUtc = now,
                IsDelete = false
            });
        }

        if (toInsert.Count == 0)
        {
            return;
        }

        _dbContext.MessageReadReceipts.AddRange(toInsert);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
