using WebApi.Models;

namespace WebApi.Contracts.ComAllocations;

public sealed class ComAllocationResponse
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public List<string> ComList { get; init; } = new();
    public DateTime CreateTime { get; init; }
    public DateTime UpdateTime { get; init; }
    public string? Remark { get; init; }

    public static ComAllocationResponse From(UserComAllocation allocation, string? userName = null)
    {
        // 解析 ComListJson
        List<string> comList;
        try
        {
            comList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(allocation.ComListJson) ?? new();
        }
        catch
        {
            comList = new();
        }

        return new ComAllocationResponse
        {
            Id = allocation.Id,
            UserId = allocation.UserId,
            UserName = userName ?? allocation.User?.UserName ?? string.Empty,
            DeviceId = allocation.DeviceId,
            ComList = comList,
            CreateTime = allocation.CreateTime,
            UpdateTime = allocation.UpdateTime,
            Remark = allocation.Remark
        };
    }
}
