using System.ComponentModel.DataAnnotations;

namespace WebApi.Contracts.DeviceCom;

public sealed class UpsertDeviceComSnapshotRequest
{
    [Required]
    public List<DeviceComPortDto> Ports { get; set; } = [];
}
