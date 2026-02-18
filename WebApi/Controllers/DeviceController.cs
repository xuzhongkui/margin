using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApi.Contracts.DeviceCom;
using WebApi.Data;
using WebApi.Hubs;
using WebApi.Models;

namespace WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeviceController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SmsManageDbContext _dbContext;
    private readonly IHubContext<DeviceHub> _hubContext;
    private readonly ILogger<DeviceController> _logger;

    public DeviceController(SmsManageDbContext dbContext, IHubContext<DeviceHub> hubContext, ILogger<DeviceController> logger)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Trigger a specific device to scan COM ports
    /// </summary>
    [HttpPost("scan-com-ports/{deviceId}")]
    public async Task<IActionResult> TriggerComPortScan(string deviceId)
    {
        try
        {
            _logger.LogInformation($"📤 [WebApi] Sending scan request to device: {deviceId}");
            _logger.LogInformation($"📤 [WebApi] Broadcasting to ALL clients via SignalR...");
            
            await _hubContext.Clients.All.SendAsync("ScanComPorts", deviceId);
            
            _logger.LogInformation($"✅ [WebApi] Scan request broadcasted successfully");
            _logger.LogInformation($"📤 [WebApi] Event: ScanComPorts, Parameter: {deviceId}");
            
            return Ok(new { message = $"Scan request sent to device: {deviceId}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ [WebApi] Error triggering COM port scan for device: {deviceId}");
            return StatusCode(500, new { error = "Failed to trigger scan" });
        }
    }

    /// <summary>
    /// Get all connected devices
    /// </summary>
    [HttpGet("connected")]
    public IActionResult GetConnectedDevices()
    {
        // 当前实现依赖 Hub 内部的静态连接表（单实例可用；多实例需分布式存储）。
        var devices = DeviceHub.GetConnectedDeviceIdsSnapshot();
        return Ok(devices);
    }

    /// <summary>
    /// 保存/更新某设备的 COM 信息快照（覆盖式更新）。
    /// DeviceId 唯一：存在则删除后重建；不存在则直接插入。
    /// </summary>
    [HttpPost("com-snapshot/{deviceId}")]
    public async Task<IActionResult> UpsertComSnapshot(string deviceId, [FromBody] UpsertDeviceComSnapshotRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest("deviceId is required");
        }

        if (request?.Ports is null)
        {
            return BadRequest("ports is required");
        }

        // 业务规则：deviceId 以路由为准，避免前端误传/串号。
        var ports = request.Ports
            .Select(p => new DeviceComPortDto
            {
                DeviceId = deviceId,
                PortName = p.PortName,
                IsAvailable = p.IsAvailable,
                IsSmsModem = p.IsSmsModem,
                ModemInfo = p.ModemInfo,
                Raw = p.Raw
            })
            .ToList();

        var dataJson = JsonSerializer.Serialize(ports, JsonOptions);

        // 覆盖式更新：不走软删除，直接硬删除再插入，保证逻辑简单且与“只有一份快照”的约束一致。
        var existing = await _dbContext.DeviceComSnapshots
            .Where(x => x.DeviceId == deviceId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _dbContext.DeviceComSnapshots.RemoveRange(existing);
        }

        _dbContext.DeviceComSnapshots.Add(new DeviceComSnapshot
        {
            DeviceId = deviceId,
            DataJson = dataJson
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { deviceId });
    }

    /// <summary>
    /// 获取某设备的 COM 信息快照
    /// </summary>
    [HttpGet("com-snapshot/{deviceId}")]
    public async Task<IActionResult> GetComSnapshot(string deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest("deviceId is required");
        }

        var snapshot = await _dbContext.DeviceComSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId, cancellationToken);

        if (snapshot is null)
        {
            return Ok(Array.Empty<DeviceComPortDto>());
        }

        try
        {
            var ports = JsonSerializer.Deserialize<List<DeviceComPortDto>>(snapshot.DataJson, JsonOptions) ?? [];

            // 以路由为准，修正 DeviceId
            ports = ports.Select(p => p with { DeviceId = deviceId }).ToList();
            return Ok(ports);
        }
        catch
        {
            // 数据损坏时不抛 500，返回空数组（前端可提示用户重新扫描/保存）。
            return Ok(Array.Empty<DeviceComPortDto>());
        }
    }
}
