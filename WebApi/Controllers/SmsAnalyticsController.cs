using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApi.Data;

namespace WebApi.Controllers;

/// <summary>
/// 短信统计和分析API
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SmsAnalyticsController : ControllerBase
{
    private readonly SmsManageDbContext _dbContext;
    private readonly ILogger<SmsAnalyticsController> _logger;

    public SmsAnalyticsController(
        SmsManageDbContext dbContext,
        ILogger<SmsAnalyticsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 获取短信接收趋势（按小时/天/月统计）
    /// </summary>
    [HttpGet("trend")]
    public async Task<IActionResult> GetSmsTrend(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] string groupBy = "hour", // hour, day, month
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.SmsMessages.AsNoTracking();

            // 默认查询最近7天
            if (!startTime.HasValue)
            {
                startTime = DateTime.UtcNow.AddDays(-7);
            }

            if (!endTime.HasValue)
            {
                endTime = DateTime.UtcNow;
            }

            query = query.Where(x => x.ReceivedTime >= startTime.Value && x.ReceivedTime <= endTime.Value);

            List<object> trend;

            switch (groupBy.ToLower())
            {
                case "hour":
                    trend = await query
                        .GroupBy(x => new
                        {
                            Year = x.ReceivedTime.Year,
                            Month = x.ReceivedTime.Month,
                            Day = x.ReceivedTime.Day,
                            Hour = x.ReceivedTime.Hour
                        })
                        .Select(g => new
                        {
                            time = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour, 0, 0),
                            count = g.Count()
                        })
                        .OrderBy(x => x.time)
                        .Cast<object>()
                        .ToListAsync(cancellationToken);
                    break;

                case "day":
                    trend = await query
                        .GroupBy(x => new
                        {
                            Year = x.ReceivedTime.Year,
                            Month = x.ReceivedTime.Month,
                            Day = x.ReceivedTime.Day
                        })
                        .Select(g => new
                        {
                            time = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day),
                            count = g.Count()
                        })
                        .OrderBy(x => x.time)
                        .Cast<object>()
                        .ToListAsync(cancellationToken);
                    break;

                case "month":
                    trend = await query
                        .GroupBy(x => new
                        {
                            Year = x.ReceivedTime.Year,
                            Month = x.ReceivedTime.Month
                        })
                        .Select(g => new
                        {
                            time = new DateTime(g.Key.Year, g.Key.Month, 1),
                            count = g.Count()
                        })
                        .OrderBy(x => x.time)
                        .Cast<object>()
                        .ToListAsync(cancellationToken);
                    break;

                default:
                    return BadRequest(new { message = "Invalid groupBy parameter. Use 'hour', 'day', or 'month'." });
            }

            return Ok(new
            {
                startTime,
                endTime,
                groupBy,
                trend
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get SMS trend");
            return StatusCode(500, new { message = "Failed to get SMS trend" });
        }
    }

    /// <summary>
    /// 获取设备活跃度统计
    /// </summary>
    [HttpGet("device-activity")]
    public async Task<IActionResult> GetDeviceActivity(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.SmsMessages.AsNoTracking();

            if (startTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime <= endTime.Value);
            }

            var deviceActivity = await query
                .GroupBy(x => x.DeviceId)
                .Select(g => new
                {
                    deviceId = g.Key,
                    totalMessages = g.Count(),
                    uniqueSenders = g.Select(x => x.SenderNumber).Distinct().Count(),
                    comPorts = g.Select(x => x.ComPort).Distinct().ToList(),
                    firstMessageTime = g.Min(x => x.ReceivedTime),
                    lastMessageTime = g.Max(x => x.ReceivedTime)
                })
                .OrderByDescending(x => x.totalMessages)
                .ToListAsync(cancellationToken);

            return Ok(deviceActivity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get device activity");
            return StatusCode(500, new { message = "Failed to get device activity" });
        }
    }

    /// <summary>
    /// 获取热门发件人排行
    /// </summary>
    [HttpGet("top-senders")]
    public async Task<IActionResult> GetTopSenders(
        [FromQuery] int top = 20,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.SmsMessages.AsNoTracking();

            if (startTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime <= endTime.Value);
            }

            var topSenders = await query
                .GroupBy(x => x.SenderNumber)
                .Select(g => new
                {
                    senderNumber = g.Key,
                    messageCount = g.Count(),
                    devices = g.Select(x => x.DeviceId).Distinct().ToList(),
                    comPorts = g.Select(x => x.ComPort).Distinct().ToList(),
                    firstMessageTime = g.Min(x => x.ReceivedTime),
                    lastMessageTime = g.Max(x => x.ReceivedTime)
                })
                .OrderByDescending(x => x.messageCount)
                .Take(top)
                .ToListAsync(cancellationToken);

            return Ok(topSenders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get top senders");
            return StatusCode(500, new { message = "Failed to get top senders" });
        }
    }

    /// <summary>
    /// 获取COM口使用统计
    /// </summary>
    [HttpGet("comport-usage")]
    public async Task<IActionResult> GetComPortUsage(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.SmsMessages.AsNoTracking();

            if (startTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime <= endTime.Value);
            }

            var comPortUsage = await query
                .GroupBy(x => new { x.DeviceId, x.ComPort })
                .Select(g => new
                {
                    deviceId = g.Key.DeviceId,
                    comPort = g.Key.ComPort,
                    messageCount = g.Count(),
                    uniqueSenders = g.Select(x => x.SenderNumber).Distinct().Count(),
                    firstMessageTime = g.Min(x => x.ReceivedTime),
                    lastMessageTime = g.Max(x => x.ReceivedTime)
                })
                .OrderByDescending(x => x.messageCount)
                .ToListAsync(cancellationToken);

            return Ok(comPortUsage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get COM port usage");
            return StatusCode(500, new { message = "Failed to get COM port usage" });
        }
    }

    /// <summary>
    /// 获取短信接收时段分布（24小时热力图）
    /// </summary>
    [HttpGet("hourly-distribution")]
    public async Task<IActionResult> GetHourlyDistribution(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.SmsMessages.AsNoTracking();

            if (startTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                query = query.Where(x => x.ReceivedTime <= endTime.Value);
            }

            var hourlyDistribution = await query
                .GroupBy(x => x.ReceivedTime.Hour)
                .Select(g => new
                {
                    hour = g.Key,
                    count = g.Count()
                })
                .OrderBy(x => x.hour)
                .ToListAsync(cancellationToken);

            // 填充缺失的小时（0-23）
            var allHours = Enumerable.Range(0, 24)
                .Select(hour => new
                {
                    hour,
                    count = hourlyDistribution.FirstOrDefault(x => x.hour == hour)?.count ?? 0
                })
                .ToList();

            return Ok(allHours);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get hourly distribution");
            return StatusCode(500, new { message = "Failed to get hourly distribution" });
        }
    }

    /// <summary>
    /// 获取综合仪表盘数据
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.SmsMessages.AsNoTracking();

            // 默认查询最近7天
            if (!startTime.HasValue)
            {
                startTime = DateTime.UtcNow.AddDays(-7);
            }

            if (!endTime.HasValue)
            {
                endTime = DateTime.UtcNow;
            }

            query = query.Where(x => x.ReceivedTime >= startTime.Value && x.ReceivedTime <= endTime.Value);

            // 总短信数
            var totalMessages = await query.CountAsync(cancellationToken);

            // 活跃设备数
            var activeDevices = await query.Select(x => x.DeviceId).Distinct().CountAsync(cancellationToken);

            // 唯一发件人数
            var uniqueSenders = await query.Select(x => x.SenderNumber).Distinct().CountAsync(cancellationToken);

            // 活跃COM口数
            var activeComPorts = await query.Select(x => new { x.DeviceId, x.ComPort }).Distinct().CountAsync(cancellationToken);

            // 今日短信数
            var todayStart = DateTime.UtcNow.Date;
            var todayMessages = await _dbContext.SmsMessages
                .AsNoTracking()
                .Where(x => x.ReceivedTime >= todayStart)
                .CountAsync(cancellationToken);

            // 昨日短信数
            var yesterdayStart = todayStart.AddDays(-1);
            var yesterdayMessages = await _dbContext.SmsMessages
                .AsNoTracking()
                .Where(x => x.ReceivedTime >= yesterdayStart && x.ReceivedTime < todayStart)
                .CountAsync(cancellationToken);

            // 增长率
            var growthRate = yesterdayMessages > 0
                ? ((todayMessages - yesterdayMessages) / (double)yesterdayMessages * 100)
                : 0;

            // 最近5条短信
            var recentMessages = await _dbContext.SmsMessages
                .AsNoTracking()
                .OrderByDescending(x => x.ReceivedTime)
                .Take(5)
                .Select(x => new
                {
                    x.Id,
                    x.DeviceId,
                    x.ComPort,
                    x.SenderNumber,
                    x.MessageContent,
                    x.ReceivedTime
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                period = new { startTime, endTime },
                summary = new
                {
                    totalMessages,
                    activeDevices,
                    uniqueSenders,
                    activeComPorts,
                    todayMessages,
                    yesterdayMessages,
                    growthRate = Math.Round(growthRate, 2)
                },
                recentMessages
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dashboard data");
            return StatusCode(500, new { message = "Failed to get dashboard data" });
        }
    }
}
