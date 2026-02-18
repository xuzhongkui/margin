using System.ComponentModel.DataAnnotations;

namespace WebApi.Contracts.ComAllocations;

public sealed class UpdateComAllocationRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(200)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    public List<string> ComList { get; set; } = new();
}
