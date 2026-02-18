using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using WebApi.Data;
using WebApi.Models;
using WebApi.Services.Infrastructure;

namespace WebApi.Services.Auth;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly IDatabase _database;
    private readonly SmsManageDbContext _dbContext;
    private readonly JwtOptions _jwtOptions;
    private readonly string _keyPrefix;

    public RefreshTokenService(
        IConnectionMultiplexer multiplexer,
        SmsManageDbContext dbContext,
        IOptions<JwtOptions> jwtOptions,
        IOptions<RedisOptions> redisOptions)
    {
        _dbContext = dbContext;
        _jwtOptions = jwtOptions.Value;

        var redisConfig = redisOptions.Value;
        _database = multiplexer.GetDatabase(redisConfig.Database);
        _keyPrefix = $"{redisConfig.InstanceName}:refresh:";
    }

    public async Task<string> CreateAsync(User user, CancellationToken cancellationToken)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var refreshToken = WebEncoders.Base64UrlEncode(tokenBytes);
        var key = _keyPrefix + refreshToken;
        var ttl = TimeSpan.FromDays(_jwtOptions.RefreshTokenDays);

        await _database.StringSetAsync(key, user.Id.ToString(), ttl);
        return refreshToken;
    }

    public async Task<User?> ValidateAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var key = _keyPrefix + refreshToken;
        var userIdValue = await _database.StringGetAsync(key);
        if (!userIdValue.HasValue || !Guid.TryParse(userIdValue, out var userId))
        {
            return null;
        }

        return await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }

    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Task.CompletedTask;
        }

        var key = _keyPrefix + refreshToken;
        return _database.KeyDeleteAsync(key);
    }
}
