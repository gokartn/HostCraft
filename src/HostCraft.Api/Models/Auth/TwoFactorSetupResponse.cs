namespace HostCraft.Api.Models.Auth;

public class TwoFactorSetupResponse
{
    public bool IsEnabled { get; set; }
    public string? QrCodeUri { get; set; }
    public string? ManualEntryKey { get; set; }
}
