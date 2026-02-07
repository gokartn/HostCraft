namespace HostCraft.Api.Models.Auth;

public class UserDto
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public string? Name { get; set; }
    public bool IsAdmin { get; set; }
}
