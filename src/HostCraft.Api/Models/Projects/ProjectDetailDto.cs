namespace HostCraft.Api.Models.Projects;

public record ProjectDetailDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<ProjectApplicationDto> Applications { get; init; } = new();
}
