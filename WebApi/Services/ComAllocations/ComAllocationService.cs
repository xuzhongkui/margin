using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Services.ComAllocations;

public interface IComAllocationService
{
    Task<List<UserComAllocation>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<UserComAllocation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<UserComAllocation>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserComAllocation> CreateAsync(Guid userId, string deviceId, List<string> comList, CancellationToken cancellationToken = default);
    Task<UserComAllocation> UpdateAsync(Guid id, Guid userId, string deviceId, List<string> comList, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class ComAllocationService : IComAllocationService
{
    private readonly SmsManageDbContext _dbContext;

    public ComAllocationService(SmsManageDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<UserComAllocation>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserComAllocations
            .Include(x => x.User)
            .AsNoTracking()
            .OrderBy(x => x.CreateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserComAllocation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserComAllocations
            .Include(x => x.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<List<UserComAllocation>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserComAllocations
            .Include(x => x.User)
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreateTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserComAllocation> CreateAsync(
        Guid userId,
        string deviceId,
        List<string> comList,
        CancellationToken cancellationToken = default)
    {
        // 验证用户是否存在
        var userExists = await _dbContext.Users
            .AnyAsync(x => x.Id == userId, cancellationToken);

        if (!userExists)
        {
            throw new InvalidOperationException("User not found");
        }

        // 序列化 COM 列表为 JSON
        var comListJson = System.Text.Json.JsonSerializer.Serialize(comList);

        var allocation = new UserComAllocation
        {
            UserId = userId,
            DeviceId = deviceId,
            ComListJson = comListJson
        };

        _dbContext.UserComAllocations.Add(allocation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 重新加载以包含导航属性
        return await GetByIdAsync(allocation.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve created allocation");
    }

    public async Task<UserComAllocation> UpdateAsync(
        Guid id,
        Guid userId,
        string deviceId,
        List<string> comList,
        CancellationToken cancellationToken = default)
    {
        var allocation = await _dbContext.UserComAllocations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (allocation is null)
        {
            throw new InvalidOperationException("Allocation not found");
        }

        // 验证用户是否存在
        var userExists = await _dbContext.Users
            .AnyAsync(x => x.Id == userId, cancellationToken);

        if (!userExists)
        {
            throw new InvalidOperationException("User not found");
        }

        // 序列化 COM 列表为 JSON
        var comListJson = System.Text.Json.JsonSerializer.Serialize(comList);

        allocation.UserId = userId;
        allocation.DeviceId = deviceId;
        allocation.ComListJson = comListJson;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 重新加载以包含导航属性
        return await GetByIdAsync(allocation.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated allocation");
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var allocation = await _dbContext.UserComAllocations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (allocation is null)
        {
            return false;
        }

        // 软删除
        allocation.IsDelete = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
