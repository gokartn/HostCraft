namespace HostCraft.Api.Models.Auth;

public record RegisterRequest(string Email, string Password, string? Name);
