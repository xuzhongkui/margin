using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApi.Contracts.Notes;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class NotesController : ControllerBase
{
    private readonly SmsManageDbContext _dbContext;

    public NotesController(SmsManageDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取所有记事本
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NoteResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var notes = await _dbContext.Notes
            .AsNoTracking()
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdateTime)
            .Select(x => NoteResponse.From(x))
            .ToListAsync(cancellationToken);

        return Ok(notes);
    }

    /// <summary>
    /// 根据ID获取记事本
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NoteResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var note = await _dbContext.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        return Ok(NoteResponse.From(note));
    }

    /// <summary>
    /// 创建记事本
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<NoteResponse>> Create(CreateNoteRequest request, CancellationToken cancellationToken)
    {
        var note = new Note
        {
            Title = request.Title,
            Content = request.Content,
            UserId = request.UserId,
            Tags = request.Tags,
            IsPinned = request.IsPinned,
            Remark = request.Remark
        };

        _dbContext.Notes.Add(note);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = note.Id }, NoteResponse.From(note));
    }

    /// <summary>
    /// 更新记事本
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NoteResponse>> Update(Guid id, UpdateNoteRequest request, CancellationToken cancellationToken)
    {
        var note = await _dbContext.Notes
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        note.Title = request.Title;
        note.Content = request.Content;
        note.UserId = request.UserId;
        note.Tags = request.Tags;
        note.IsPinned = request.IsPinned;
        note.Remark = request.Remark;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(NoteResponse.From(note));
    }

    /// <summary>
    /// 删除记事本（软删除）
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var note = await _dbContext.Notes
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        note.IsDelete = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// 搜索记事本
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<NoteResponse>>> Search(
        [FromQuery] string? keyword,
        [FromQuery] Guid? userId,
        [FromQuery] bool? isPinned,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Notes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x => x.Title.Contains(keyword) || x.Content.Contains(keyword) || (x.Tags != null && x.Tags.Contains(keyword)));
        }

        if (userId.HasValue)
        {
            query = query.Where(x => x.UserId == userId.Value);
        }

        if (isPinned.HasValue)
        {
            query = query.Where(x => x.IsPinned == isPinned.Value);
        }

        var notes = await query
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdateTime)
            .Select(x => NoteResponse.From(x))
            .ToListAsync(cancellationToken);

        return Ok(notes);
    }

    /// <summary>
    /// 切换置顶状态
    /// </summary>
    [HttpPatch("{id:guid}/toggle-pin")]
    public async Task<ActionResult<NoteResponse>> TogglePin(Guid id, CancellationToken cancellationToken)
    {
        var note = await _dbContext.Notes
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        note.IsPinned = !note.IsPinned;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(NoteResponse.From(note));
    }
}
