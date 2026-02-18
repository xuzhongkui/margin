using Margin.Services;

namespace Margin;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly SignalRService _signalRService;
    private readonly SmsReceiverService _smsReceiverService;
    private readonly ComPortScanner _comPortScanner;

    public Worker(
        ILogger<Worker> logger, 
        SignalRService signalRService,
        SmsReceiverService smsReceiverService,
        ComPortScanner comPortScanner)
    {
        _logger = logger;
        _signalRService = signalRService;
        _smsReceiverService = smsReceiverService;
        _comPortScanner = comPortScanner;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker starting...");

        try
        {
            // Start SignalR connection
            await _signalRService.StartAsync(stoppingToken);
            _logger.LogInformation("SignalR service started successfully");

            // 不再自动启动短信监听,等待通过 SignalR API 手动启动
            _logger.LogInformation("SMS receiver is ready. Waiting for StartSmsReceiver command via SignalR...");

            // Keep the worker running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(5000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in worker execution");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker stopping...");
        _smsReceiverService.Stop();
        await _signalRService.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
