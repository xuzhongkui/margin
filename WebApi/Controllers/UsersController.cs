using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApi.Contracts.Users;
using WebApi.Data;
using WebApi.Models;
using WebApi.Services.Auth;
using WebApi.Services.Security;

namespace WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UsersController : ControllerBase
{
    private readonly SmsManageDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenService _refreshTokenService;

    public UsersController(
        SmsManageDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IRefreshTokenService refreshTokenService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _refreshTokenService = refreshTokenService;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .OrderBy(x => x.UserName)
            .Select(x => UserResponse.From(x))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        return Ok(UserResponse.From(user));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Users
            .AnyAsync(x => x.UserName == request.UserName, cancellationToken);

        if (exists)
        {
            return Conflict("UserName already exists.");
        }

        var salt = _passwordHasher.GenerateSalt();
        var user = new User
        {
            UserName = request.UserName,
            PasswordSalt = salt,
            PasswordHash = _passwordHasher.Hash(request.Password, salt),
            Role = request.Role,
            Remark = request.Remark
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, UserResponse.From(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.UserName == request.UserName, cancellationToken);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordSalt, user.PasswordHash))
        {
            return Unauthorized();
        }

        var token = _jwtTokenService.CreateToken(user);
        var refreshToken = await _refreshTokenService.CreateAsync(user, cancellationToken);

        return Ok(new LoginResponse
        {
            AccessToken = token,
            RefreshToken = refreshToken,
            User = UserResponse.From(user)
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var user = await _refreshTokenService.ValidateAsync(request.RefreshToken, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        await _refreshTokenService.RevokeAsync(request.RefreshToken, cancellationToken);

        var token = _jwtTokenService.CreateToken(user);
        var refreshToken = await _refreshTokenService.CreateAsync(user, cancellationToken);

        return Ok(new LoginResponse
        {
            AccessToken = token,
            RefreshToken = refreshToken,
            User = UserResponse.From(user)
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        // Check if username is being changed and if it already exists
        if (user.UserName != request.UserName)
        {
            var exists = await _dbContext.Users
                .AnyAsync(x => x.UserName == request.UserName && x.Id != id, cancellationToken);

            if (exists)
            {
                return Conflict("UserName already exists.");
            }
        }

        user.UserName = request.UserName;
        user.Role = request.Role;
        user.Remark = request.Remark;
        user.UpdateTime = DateTime.UtcNow;

        // Update password if provided
        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            var salt = _passwordHasher.GenerateSalt();
            user.PasswordSalt = salt;
            user.PasswordHash = _passwordHasher.Hash(request.NewPassword, salt);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(UserResponse.From(user));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        // Soft delete
        user.IsDelete = true;
        user.UpdateTime = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet("search")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<UserResponse>>> Search(
        [FromQuery] string? keyword,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Users.AsNoTracking().Where(x => !x.IsDelete);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x => x.UserName.Contains(keyword) || (x.Remark != null && x.Remark.Contains(keyword)));
        }

        var users = await query
            .OrderBy(x => x.UserName)
            .Select(x => UserResponse.From(x))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }
}
