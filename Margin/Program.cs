using Margin;
using Margin.Services;

var builder = Host.CreateApplicationBuilder(args);
// ComPortScanner 需要 IConfiguration 读取配置（波特率等）
builder.Services.AddSingleton<ComPortScanner>();
builder.Services.AddSingleton<SignalRService>();
builder.Services.AddSingleton<SmsReceiverService>();
builder.Services.AddSingleton<SmsSenderService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
