namespace HostCraft.Api.Models.Services;

public record ScaleServiceRequest
{
    public int Replicas { get; init; }
}
