using System.Text.Json;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Backups;

/// <summary>
/// Local filesystem storage provider for backups (useful for testing and local development)
/// </summary>
public class LocalFileSystemBackupStorageProvider : IBackupStorageProvider
{
    private readonly ILogger<LocalFileSystemBackupStorageProvider> _logger;
    private readonly ISshService _sshService;
    private readonly Server _server;
    private readonly string _storagePath;

    public LocalFileSystemBackupStorageProvider(
        ILogger<LocalFileSystemBackupStorageProvider> logger,
        ISshService sshService,
        Server server,
        string storagePath)
    {
        _logger = logger;
        _sshService = sshService;
        _server = server;
        _storagePath = storagePath;
    }

    public async Task<string> UploadBackupAsync(
        string localBackupPath,
        BackupManifest manifest,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Uploading backup to local filesystem: {Path}", _storagePath);

            // Ensure storage directory exists
            var mkdirResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"mkdir -p '{_storagePath}'",
                cancellationToken);

            if (mkdirResult.ExitCode != 0)
            {
                throw new Exception($"Failed to create storage directory: {mkdirResult.Error}");
            }

            // Get file size for progress reporting
            var sizeResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"stat -c%s '{localBackupPath}' 2>/dev/null || echo 0",
                cancellationToken);

            var fileSize = long.TryParse(sizeResult.Output.Trim(), out var size) ? size : 0;

            // Copy file to storage location
            var fileName = Path.GetFileName(localBackupPath);
            var destinationPath = $"{_storagePath}/{fileName}";

            progress?.Report(new BackupProgress(0, fileSize, fileName, "Copying to storage"));

            var copyResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"cp '{localBackupPath}' '{destinationPath}'",
                cancellationToken);

            if (copyResult.ExitCode != 0)
            {
                throw new Exception($"Failed to copy backup: {copyResult.Error}");
            }

            progress?.Report(new BackupProgress(fileSize, fileSize, fileName, "Upload complete"));

            _logger.LogInformation("Backup uploaded successfully to {Path}", destinationPath);
            return destinationPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload backup to local filesystem");
            throw;
        }
    }

    public async Task<string> DownloadBackupAsync(
        string remoteBackupPath,
        string localDestinationPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading backup from local filesystem: {Path}", remoteBackupPath);

            // Get file size for progress reporting
            var sizeResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"stat -c%s '{remoteBackupPath}' 2>/dev/null || echo 0",
                cancellationToken);

            var fileSize = long.TryParse(sizeResult.Output.Trim(), out var size) ? size : 0;
            var fileName = Path.GetFileName(remoteBackupPath);

            progress?.Report(new BackupProgress(0, fileSize, fileName, "Copying from storage"));

            // Ensure destination directory exists
            var destDir = Path.GetDirectoryName(localDestinationPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                await _sshService.ExecuteCommandAsync(
                    _server,
                    $"mkdir -p '{destDir}'",
                    cancellationToken);
            }

            // Copy file from storage location
            var copyResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"cp '{remoteBackupPath}' '{localDestinationPath}'",
                cancellationToken);

            if (copyResult.ExitCode != 0)
            {
                throw new Exception($"Failed to copy backup: {copyResult.Error}");
            }

            progress?.Report(new BackupProgress(fileSize, fileSize, fileName, "Download complete"));

            _logger.LogInformation("Backup downloaded successfully to {Path}", localDestinationPath);
            return localDestinationPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download backup from local filesystem");
            throw;
        }
    }

    public async Task<List<RemoteBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing backups in local filesystem: {Path}", _storagePath);

            // List all .tar.gz files in storage directory
            var listResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"find '{_storagePath}' -maxdepth 1 -name '*.tar.gz' -type f -printf '%p|%s|%T@\\n' 2>/dev/null || echo ''",
                cancellationToken);

            if (listResult.ExitCode != 0 || string.IsNullOrWhiteSpace(listResult.Output))
            {
                return new List<RemoteBackupInfo>();
            }

            var backups = new List<RemoteBackupInfo>();
            var lines = listResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length != 3) continue;

                var path = parts[0];
                var sizeBytes = long.TryParse(parts[1], out var size) ? size : 0;
                var timestamp = double.TryParse(parts[2], out var ts) ? ts : 0;
                var createdAt = DateTimeOffset.FromUnixTimeSeconds((long)timestamp).DateTime;

                // Extract backup ID from filename (assumes format: hostcraft-*-backup-{timestamp}.tar.gz)
                var fileName = Path.GetFileName(path);
                var backupId = fileName.Replace(".tar.gz", "");

                // Try to get checksum if available
                var checksumResult = await _sshService.ExecuteCommandAsync(
                    _server,
                    $"sha256sum '{path}' 2>/dev/null | awk '{{print $1}}' || echo ''",
                    cancellationToken);

                var checksum = checksumResult.ExitCode == 0 ? checksumResult.Output.Trim() : null;

                backups.Add(new RemoteBackupInfo(path, backupId, createdAt, sizeBytes, checksum));
            }

            _logger.LogInformation("Found {Count} backups in local filesystem", backups.Count);
            return backups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list backups from local filesystem");
            throw;
        }
    }

    public async Task<bool> DeleteBackupAsync(string remoteBackupPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting backup from local filesystem: {Path}", remoteBackupPath);

            var deleteResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"rm -f '{remoteBackupPath}'",
                cancellationToken);

            if (deleteResult.ExitCode != 0)
            {
                _logger.LogWarning("Failed to delete backup: {Error}", deleteResult.Error);
                return false;
            }

            _logger.LogInformation("Backup deleted successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete backup from local filesystem");
            return false;
        }
    }

    public async Task<bool> VerifyBackupExistsAsync(string remoteBackupPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var testResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"test -f '{remoteBackupPath}' && echo 'exists' || echo 'not found'",
                cancellationToken);

            return testResult.Output.Trim() == "exists";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify backup existence");
            return false;
        }
    }

    public async Task<BackupManifest?> GetBackupManifestAsync(
        string remoteBackupPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Reading manifest from backup: {Path}", remoteBackupPath);

            // Extract manifest.json from tar.gz without extracting entire archive
            var extractResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"tar -xzf '{remoteBackupPath}' -O manifest.json 2>/dev/null || echo ''",
                cancellationToken);

            if (extractResult.ExitCode != 0 || string.IsNullOrWhiteSpace(extractResult.Output))
            {
                _logger.LogWarning("Failed to extract manifest from backup");
                return null;
            }

            var manifest = JsonSerializer.Deserialize<BackupManifest>(extractResult.Output);
            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read backup manifest");
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Testing local filesystem storage connection");

            // Test if we can access and write to the storage path
            var testResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"mkdir -p '{_storagePath}' && touch '{_storagePath}/.test' && rm '{_storagePath}/.test' && echo 'success' || echo 'failed'",
                cancellationToken);

            var success = testResult.Output.Trim() == "success";
            
            if (success)
            {
                _logger.LogInformation("Local filesystem storage connection test successful");
            }
            else
            {
                _logger.LogWarning("Local filesystem storage connection test failed");
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test local filesystem storage connection");
            return false;
        }
    }
}
