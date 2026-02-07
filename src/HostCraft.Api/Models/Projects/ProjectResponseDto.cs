namespace HostCraft.Api.Models.Projects;

public record ProjectResponseDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int ApplicationCount { get; init; }
    public DateTime CreatedAt { get; init; }
}
