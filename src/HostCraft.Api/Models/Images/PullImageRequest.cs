namespace HostCraft.Api.Models.Images;

public record PullImageRequest
{
    public string ImageName { get; init; } = string.Empty;
}
