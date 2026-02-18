using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Contracts.ComAllocations;
using WebApi.Services.ComAllocations;

namespace WebApi.Controllers;

[ApiController]
[Route("api/com-allocations")]
[Authorize]
public sealed class ComAllocationsController : ControllerBase
{
    private readonly IComAllocationService _comAllocationService;

    public ComAllocationsController(IComAllocationService comAllocationService)
    {
        _comAllocationService = comAllocationService;
    }

    /// <summary>
    /// 获取当前登录用户的COM分配
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<IEnumerable<ComAllocationResponse>>> GetMyAllocations(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "用户未登录" });
        }

        var allocations = await _comAllocationService.GetByUserIdAsync(Guid.Parse(userId), cancellationToken);
        var responses = allocations.Select(a => ComAllocationResponse.From(a));
        return Ok(responses);
    }

    /// <summary>
    /// 获取所有COM分配（管理员）
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<IEnumerable<ComAllocationResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var allocations = await _comAllocationService.GetAllAsync(cancellationToken);
        var responses = allocations.Select(a => ComAllocationResponse.From(a));
        return Ok(responses);
    }

    /// <summary>
    /// 根据ID获取COM分配（管理员）
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ComAllocationResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var allocation = await _comAllocationService.GetByIdAsync(id, cancellationToken);

        if (allocation is null)
        {
            return NotFound();
        }

        return Ok(ComAllocationResponse.From(allocation));
    }

    /// <summary>
    /// 根据用户ID获取COM分配（管理员）
    /// </summary>
    [HttpGet("user/{userId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<IEnumerable<ComAllocationResponse>>> GetByUserId(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var allocations = await _comAllocationService.GetByUserIdAsync(userId, cancellationToken);
        var responses = allocations.Select(a => ComAllocationResponse.From(a));
        return Ok(responses);
    }

    /// <summary>
    /// 创建新的COM分配
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ComAllocationResponse>> Create(
        CreateComAllocationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var allocation = await _comAllocationService.CreateAsync(
                request.UserId,
                request.DeviceId,
                request.ComList,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = allocation.Id },
                ComAllocationResponse.From(allocation));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// 更新COM分配
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ComAllocationResponse>> Update(
        Guid id,
        UpdateComAllocationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var allocation = await _comAllocationService.UpdateAsync(
                id,
                request.UserId,
                request.DeviceId,
                request.ComList,
                cancellationToken);

            return Ok(ComAllocationResponse.From(allocation));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// 删除COM分配
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var success = await _comAllocationService.DeleteAsync(id, cancellationToken);

        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }
}
