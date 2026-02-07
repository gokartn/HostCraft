using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for executing commands on remote servers via SSH.
/// </summary>
public interface ISshService
{
    Task<bool> ValidateConnectionAsync(Server server, CancellationToken cancellationToken = default);
    Task<SshCommandResult> ExecuteCommandAsync(Server server, string command, CancellationToken cancellationToken = default);
    Task<bool> UploadFileAsync(Server server, string localPath, string remotePath, CancellationToken cancellationToken = default);
    Task<bool> DownloadFileAsync(Server server, string remotePath, string localPath, CancellationToken cancellationToken = default);
}

public record SshCommandResult(int ExitCode, string Output, string Error);
