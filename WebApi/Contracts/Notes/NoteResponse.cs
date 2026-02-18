using WebApi.Models;

namespace WebApi.Contracts.Notes;

public sealed record NoteResponse
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public Guid? UserId { get; init; }
    public string? Tags { get; init; }
    public bool IsPinned { get; init; }
    public DateTime CreateTime { get; init; }
    public DateTime UpdateTime { get; init; }
    public string? Remark { get; init; }

    public static NoteResponse From(Note note)
    {
        return new NoteResponse
        {
            Id = note.Id,
            Title = note.Title,
            Content = note.Content,
            UserId = note.UserId,
            Tags = note.Tags,
            IsPinned = note.IsPinned,
            CreateTime = note.CreateTime,
            UpdateTime = note.UpdateTime,
            Remark = note.Remark
        };
    }
}
