using System.ComponentModel.DataAnnotations;

namespace WebApi.Contracts.Notes;

public sealed record UpdateNoteRequest
{
    [Required]
    [MaxLength(200)]
    public string Title { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public Guid? UserId { get; init; }

    [MaxLength(100)]
    public string? Tags { get; init; }

    public bool IsPinned { get; init; }

    [MaxLength(500)]
    public string? Remark { get; init; }
}
