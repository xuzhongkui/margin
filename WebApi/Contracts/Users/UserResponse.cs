using WebApi.Models;

namespace WebApi.Contracts.Users;

public sealed class UserResponse
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = string.Empty;
    public UserRole Role { get; init; }
    public DateTime CreateTime { get; init; }
    public DateTime UpdateTime { get; init; }
    public string? Remark { get; init; }

    public static UserResponse From(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            UserName = user.UserName,
            Role = user.Role,
            CreateTime = user.CreateTime,
            UpdateTime = user.UpdateTime,
            Remark = user.Remark
        };
    }
}
