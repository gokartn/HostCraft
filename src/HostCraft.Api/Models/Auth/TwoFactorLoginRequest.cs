namespace HostCraft.Api.Models.Auth;

public record TwoFactorLoginRequest(string Email, string Code);
