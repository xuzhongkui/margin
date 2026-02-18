using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WebApi.Hubs;

namespace WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SmsReceiverController : ControllerBase
{
    private readonly IHubContext<DeviceHub> _hubContext;
    private readonly ILogger<SmsReceiverController> _logger;

    public SmsReceiverController(IHubContext<DeviceHub> hubContext, ILogger<SmsReceiverController> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// 启动指定设备的短信监听
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="request">监听配置（COM口列表）</param>
    [HttpPost("start/{deviceId}")]
    public async Task<IActionResult> StartSmsReceiver(string deviceId, [FromBody] StartSmsReceiverRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return BadRequest(new { error = "deviceId is required" });
            }

            if (request?.Ports == null || request.Ports.Count == 0)
            {
                return BadRequest(new { error = "At least one COM port is required" });
            }

            _logger.LogInformation($"📤 [WebApi] Sending StartSmsReceiver request to device: {deviceId}");
            _logger.LogInformation($"📤 [WebApi] COM ports: {string.Join(", ", request.Ports.Select(p => $"{p.PortName}@{p.BaudRate}"))}");

            // 通过 SignalR 发送启动命令到边缘设备
            await _hubContext.Clients.All.SendAsync("StartSmsReceiver", deviceId, request.Ports);

            _logger.LogInformation($"✅ [WebApi] StartSmsReceiver request sent successfully");

            return Ok(new
            {
                message = $"SMS receiver start request sent to device: {deviceId}",
                ports = request.Ports
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ [WebApi] Error starting SMS receiver for device: {deviceId}");
            return StatusCode(500, new { error = "Failed to start SMS receiver" });
        }
    }

    /// <summary>
    /// 停止指定设备的短信监听
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    [HttpPost("stop/{deviceId}")]
    public async Task<IActionResult> StopSmsReceiver(string deviceId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return BadRequest(new { error = "deviceId is required" });
            }

            _logger.LogInformation($"📤 [WebApi] Sending StopSmsReceiver request to device: {deviceId}");

            // 通过 SignalR 发送停止命令到边缘设备
            await _hubContext.Clients.All.SendAsync("StopSmsReceiver", deviceId);

            _logger.LogInformation($"✅ [WebApi] StopSmsReceiver request sent successfully");

            return Ok(new { message = $"SMS receiver stop request sent to device: {deviceId}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ [WebApi] Error stopping SMS receiver for device: {deviceId}");
            return StatusCode(500, new { error = "Failed to stop SMS receiver" });
        }
    }

    /// <summary>
    /// 停止所有设备的短信监听
    /// </summary>
    [HttpPost("stop")]
    public async Task<IActionResult> StopSmsReceiverAll()
    {
        try
        {
            _logger.LogInformation("📤 [WebApi] Sending StopSmsReceiver request to all devices");

            // 通过 SignalR 发送停止命令到所有边缘设备
            await _hubContext.Clients.All.SendAsync("StopSmsReceiver", string.Empty);

            _logger.LogInformation("✅ [WebApi] StopSmsReceiver request sent successfully to all devices");

            return Ok(new { message = "SMS receiver stop request sent to all devices" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [WebApi] Error stopping SMS receiver for all devices");
            return StatusCode(500, new { error = "Failed to stop SMS receiver" });
        }
    }
}

/// <summary>
/// 启动短信监听请求
/// </summary>
public class StartSmsReceiverRequest
{
    /// <summary>
    /// 需要监听的 COM 口列表
    /// </summary>
    public List<ComPortConfig> Ports { get; set; } = new();
}

/// <summary>
/// COM 口配置
/// </summary>
public class ComPortConfig
{
    /// <summary>
    /// COM 口名称（如 COM1）
    /// </summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>
    /// 波特率（如 115200）
    /// </summary>
    public int BaudRate { get; set; } = 115200;
}

