using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace HostCraft.Infrastructure.Ssh;

public class SshService : ISshService
{
    private readonly ILogger<SshService> _logger;
    private readonly Dictionary<string, SshClient> _sshClients = new();
    private readonly object _lock = new();

    public SshService(ILogger<SshService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ValidateConnectionAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetOrCreateClient(server);
            if (!client.IsConnected)
            {
                await Task.Run(() => client.Connect(), cancellationToken);
            }
            return client.IsConnected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate SSH connection to {Host}:{Port}", server.Host, server.Port);
            return false;
        }
    }

    public async Task<SshCommandResult> ExecuteCommandAsync(Server server, string command, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetOrCreateClient(server);
            if (!client.IsConnected)
            {
                await Task.Run(() => client.Connect(), cancellationToken);
            }

            var cmd = client.CreateCommand(command);
            var result = await Task.Run(() => cmd.Execute(), cancellationToken);
            
            return new SshCommandResult(cmd.ExitStatus, result, cmd.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute SSH command on {Host}:{Port}", server.Host, server.Port);
            return new SshCommandResult(-1, string.Empty, ex.Message);
        }
    }

    public async Task<bool> UploadFileAsync(Server server, string localPath, string remotePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetOrCreateClient(server);
            if (!client.IsConnected)
            {
                await Task.Run(() => client.Connect(), cancellationToken);
            }

            var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, GetAuthenticationMethod(server));
            using var sftpClient = new SftpClient(connectionInfo);
            if (!sftpClient.IsConnected)
            {
                await Task.Run(() => sftpClient.Connect(), cancellationToken);
            }

            using var fileStream = File.OpenRead(localPath);
            await Task.Run(() => sftpClient.UploadFile(fileStream, remotePath), cancellationToken);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to {Host}:{Port}", server.Host, server.Port);
            return false;
        }
    }

    public async Task<bool> DownloadFileAsync(Server server, string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetOrCreateClient(server);
            if (!client.IsConnected)
            {
                await Task.Run(() => client.Connect(), cancellationToken);
            }

            var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, GetAuthenticationMethod(server));
            using var sftpClient = new SftpClient(connectionInfo);
            if (!sftpClient.IsConnected)
            {
                await Task.Run(() => sftpClient.Connect(), cancellationToken);
            }

            using var fileStream = File.Create(localPath);
            await Task.Run(() => sftpClient.DownloadFile(remotePath, fileStream), cancellationToken);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file from {Host}:{Port}", server.Host, server.Port);
            return false;
        }
    }

    private SshClient GetOrCreateClient(Server server)
    {
        var key = $"{server.Host}:{server.Port}:{server.Username}";
        
        lock (_lock)
        {
            if (_sshClients.TryGetValue(key, out var existingClient))
            {
                if (existingClient.IsConnected)
                {
                    return existingClient;
                }
                else
                {
                    // Clean up disconnected client
                    existingClient.Dispose();
                    _sshClients.Remove(key);
                }
            }

            // Create new client
            var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, GetAuthenticationMethod(server));
            var client = new SshClient(connectionInfo);
            _sshClients[key] = client;
            
            return client;
        }
    }

    private AuthenticationMethod GetAuthenticationMethod(Server server)
    {
        if (server.PrivateKey != null && !string.IsNullOrEmpty(server.PrivateKey.KeyData))
        {
            using var keyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(server.PrivateKey.KeyData));
            var keyFile = string.IsNullOrEmpty(server.PrivateKey.Passphrase)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(keyStream, server.PrivateKey.Passphrase);
            
            return new PrivateKeyAuthenticationMethod(server.Username, keyFile);
        }
        
        throw new InvalidOperationException($"Server {server.Name} does not have a private key configured");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var client in _sshClients.Values)
            {
                try
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                    client.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
            _sshClients.Clear();
        }
    }
}
