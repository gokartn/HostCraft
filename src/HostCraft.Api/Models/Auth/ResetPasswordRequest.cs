namespace HostCraft.Api.Models.Auth;

public record ResetPasswordRequest(string Email, string NewPassword);
