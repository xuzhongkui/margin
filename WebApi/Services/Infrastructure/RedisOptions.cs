namespace WebApi.Services.Infrastructure;

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";

    // Optional. If provided, we'll set it on StackExchange.Redis ConfigurationOptions.
    public string? Password { get; set; }

    public int Database { get; set; } = 6;
    public string InstanceName { get; set; } = "SmsManage";
}
