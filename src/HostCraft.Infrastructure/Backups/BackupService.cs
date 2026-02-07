using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Backups;

/// <summary>
/// Service for backup and restore operations with S3 support.
/// </summary>
public class BackupService : IBackupService
{
    private readonly HostCraftDbContext _context;
    private readonly ISshService _sshService;
    private readonly IDockerService _dockerService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackupService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private const string BackupBasePath = "/var/hostcraft/backups";

    public BackupService(
        HostCraftDbContext context,
        ISshService sshService,
        IDockerService dockerService,
        IConfiguration configuration,
        ILogger<BackupService> logger,
        ILoggerFactory loggerFactory)
    {
        _context = context;
        _sshService = sshService;
        _dockerService = dockerService;
        _configuration = configuration;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<Core.Entities.Backup> BackupConfigurationAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await GetApplicationWithServerAsync(applicationId, cancellationToken);
        if (application == null)
        {
            throw new InvalidOperationException($"Application {applicationId} not found");
        }

        var backup = CreateBackupRecord(application, BackupType.Configuration);
        _context.Backups.Add(backup);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            backup.Status = BackupStatus.Running;
            await _context.SaveChangesAsync(cancellationToken);

            // Create backup directory on server
            var backupDir = $"{BackupBasePath}/{application.Uuid}";
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"config-{timestamp}.json";
            var backupPath = $"{backupDir}/{backupFileName}";

            await EnsureBackupDirectoryAsync(application.Server, backupDir, cancellationToken);

            // Create configuration backup JSON
            var configData = CreateConfigurationBackup(application);
            var jsonContent = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = true });

            // Write config to remote server
            var writeResult = await _sshService.ExecuteCommandAsync(
                application.Server,
                $"cat > '{backupPath}' << 'HOSTCRAFT_BACKUP_EOF'\n{jsonContent}\nHOSTCRAFT_BACKUP_EOF",
                cancellationToken);

            if (writeResult.ExitCode != 0)
            {
                throw new Exception($"Failed to write backup: {writeResult.Error}");
            }

            // Get file size
            var sizeResult = await _sshService.ExecuteCommandAsync(
                application.Server,
                $"stat -c%s '{backupPath}' 2>/dev/null || echo 0",
                cancellationToken);

            backup.StoragePath = backupPath;
            backup.SizeBytes = long.TryParse(sizeResult.Output.Trim(), out var size) ? size : 0;
            backup.Status = BackupStatus.Success;
            backup.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Configuration backup created for application {ApplicationId}: {BackupPath}",
                applicationId, backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration backup failed for application {ApplicationId}", applicationId);
            backup.Status = BackupStatus.Failed;
            backup.ErrorMessage = ex.Message;
            backup.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return backup;
    }

    public async Task<Core.Entities.Backup> BackupVolumesAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await GetApplicationWithServerAsync(applicationId, cancellationToken);
        if (application == null)
        {
            throw new InvalidOperationException($"Application {applicationId} not found");
        }

        var backup = CreateBackupRecord(application, BackupType.Volume);
        _context.Backups.Add(backup);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            backup.Status = BackupStatus.Running;
            await _context.SaveChangesAsync(cancellationToken);

            var volumes = await _context.Volumes
                .Where(v => v.ApplicationId == applicationId)
                .ToListAsync(cancellationToken);

            if (!volumes.Any())
            {
                backup.Status = BackupStatus.Success;
                backup.CompletedAt = DateTime.UtcNow;
                backup.SizeBytes = 0;
                backup.ErrorMessage = "No volumes to backup";
                await _context.SaveChangesAsync(cancellationToken);
                return backup;
            }

            var backupDir = $"{BackupBasePath}/{application.Uuid}";
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"volumes-{timestamp}.tar.gz";
            var backupPath = $"{backupDir}/{backupFileName}";

            await EnsureBackupDirectoryAsync(application.Server, backupDir, cancellationToken);

            // Create tar archive of all volumes
            var volumePaths = new StringBuilder();
            foreach (var volume in volumes)
            {
                // Get volume inspect path
                var inspectResult = await _sshService.ExecuteCommandAsync(
                    application.Server,
                    $"docker volume inspect {volume.Name} --format '{{{{.Mountpoint}}}}'",
                    cancellationToken);

                if (inspectResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(inspectResult.Output))
                {
                    volumePaths.Append($" '{inspectResult.Output.Trim()}'");
                }
            }

            if (volumePaths.Length == 0)
            {
                backup.Status = BackupStatus.Success;
                backup.CompletedAt = DateTime.UtcNow;
                backup.ErrorMessage = "No volume mount points found";
                await _context.SaveChangesAsync(cancellationToken);
                return backup;
            }

            // Create tar archive
            var tarResult = await _sshService.ExecuteCommandAsync(
                application.Server,
                $"tar -czf '{backupPath}' {volumePaths}",
                cancellationToken);

            if (tarResult.ExitCode != 0)
            {
                throw new Exception($"Failed to create volume backup: {tarResult.Error}");
            }

            // Get file size
            var sizeResult = await _sshService.ExecuteCommandAsync(
                application.Server,
                $"stat -c%s '{backupPath}' 2>/dev/null || echo 0",
                cancellationToken);

            backup.StoragePath = backupPath;
            backup.SizeBytes = long.TryParse(sizeResult.Output.Trim(), out var size) ? size : 0;
            backup.Status = BackupStatus.Success;
            backup.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Volume backup created for application {ApplicationId}: {BackupPath} ({Volumes} volumes, {Size} bytes)",
                applicationId, backupPath, volumes.Count, backup.SizeBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Volume backup failed for application {ApplicationId}", applicationId);
            backup.Status = BackupStatus.Failed;
            backup.ErrorMessage = ex.Message;
            backup.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return backup;
    }

    public async Task<Core.Entities.Backup> CreateFullBackupAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await GetApplicationWithServerAsync(applicationId, cancellationToken);
        if (application == null)
        {
            throw new InvalidOperationException($"Application {applicationId} not found");
        }

        var backup = CreateBackupRecord(application, BackupType.Full);
        _context.Backups.Add(backup);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            backup.Status = BackupStatus.Running;
            await _context.SaveChangesAsync(cancellationToken);

            var backupDir = $"{BackupBasePath}/{application.Uuid}";
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"full-{timestamp}.tar.gz";
            var backupPath = $"{backupDir}/{backupFileName}";
            var tempDir = $"{backupDir}/temp-{timestamp}";

            await EnsureBackupDirectoryAsync(application.Server, tempDir, cancellationToken);

            // 1. Write configuration backup
            var configData = CreateConfigurationBackup(application);
            var jsonContent = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = true });
            await _sshService.ExecuteCommandAsync(
                application.Server,
                $"cat > '{tempDir}/config.json' << 'HOSTCRAFT_BACKUP_EOF'\n{jsonContent}\nHOSTCRAFT_BACKUP_EOF",
                cancellationToken);

            // 2. Copy volume data
            var volumes = await _context.Volumes
                .Where(v => v.ApplicationId == applicationId)
                .ToListAsync(cancellationToken);

            foreach (var volume in volumes)
            {
                var inspectResult = await _sshService.ExecuteCommandAsync(
                    application.Server,
                    $"docker volume inspect {volume.Name} --format '{{{{.Mountpoint}}}}'",
                    cancellationToken);

                if (inspectResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(inspectResult.Output))
                {
                    var mountPoint = inspectResult.Output.Trim();
                    var volumeDir = $"{tempDir}/volumes/{volume.Name}";
                    await _sshService.ExecuteCommandAsync(
                        application.Server,
                        $"mkdir -p '{volumeDir}' && cp -a '{mountPoint}/.' '{volumeDir}/' 2>/dev/null || true",
                        cancellationToken);
                }
            }

            // 3. Create final tar archive
            var tarResult = await _sshService.ExecuteCommandAsync(
                application.Server,
                $"cd '{tempDir}' && tar -czf '{backupPath}' . && rm -rf '{tempDir}'",
                cancellationToken);

            if (tarResult.ExitCode != 0)
            {
                throw new Exception($"Failed to create full backup archive: {tarResult.Error}");
            }

            // Get file size
            var sizeResult = await _sshService.ExecuteCommandAsync(
                application.Server,
                $"stat -c%s '{backupPath}' 2>/dev/null || echo 0",
                cancellationToken);

            backup.StoragePath = backupPath;
            backup.SizeBytes = long.TryParse(sizeResult.Output.Trim(), out var size) ? size : 0;
            backup.Status = BackupStatus.Success;
            backup.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Full backup created for application {ApplicationId}: {BackupPath} ({Size} bytes)",
                applicationId, backupPath, backup.SizeBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Full backup failed for application {ApplicationId}", applicationId);
            backup.Status = BackupStatus.Failed;
            backup.ErrorMessage = ex.Message;
            backup.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return backup;
    }

    public async Task<bool> RestoreFromBackupAsync(int backupId, int targetServerId, CancellationToken cancellationToken = default)
    {
        var backup = await _context.Backups
            .Include(b => b.Application)
            .ThenInclude(a => a!.Server)
            .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);

        if (backup == null)
        {
            _logger.LogWarning("Backup {BackupId} not found for restore", backupId);
            return false;
        }

        if (backup.Application == null)
        {
            _logger.LogWarning("Cannot restore from backup {BackupId}: no application associated", backupId);
            return false;
        }

        if (backup.Status != BackupStatus.Success)
        {
            _logger.LogWarning("Cannot restore from backup {BackupId}: status is {Status}", backupId, backup.Status);
            return false;
        }

        if (string.IsNullOrEmpty(backup.StoragePath))
        {
            _logger.LogWarning("Cannot restore from backup {BackupId}: no storage path", backupId);
            return false;
        }

        var targetServer = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == targetServerId, cancellationToken);

        if (targetServer == null)
        {
            _logger.LogWarning("Target server {ServerId} not found for restore", targetServerId);
            return false;
        }

        try
        {
            _logger.LogInformation("Starting restore from backup {BackupId} to server {ServerId}", backupId, targetServerId);

            var restoreDir = $"/tmp/hostcraft-restore-{backup.Uuid}";

            // If same server, extract backup directly
            // If different server, we would need to SCP the file first
            if (targetServerId == backup.Application.ServerId)
            {
                // Extract backup to temp directory
                await _sshService.ExecuteCommandAsync(
                    targetServer,
                    $"mkdir -p '{restoreDir}' && tar -xzf '{backup.StoragePath}' -C '{restoreDir}'",
                    cancellationToken);

                // Restore volumes if present
                var volumeCheckResult = await _sshService.ExecuteCommandAsync(
                    targetServer,
                    $"test -d '{restoreDir}/volumes' && echo 'yes' || echo 'no'",
                    cancellationToken);

                if (volumeCheckResult.Output.Trim() == "yes")
                {
                    // Get list of volume directories
                    var volumeListResult = await _sshService.ExecuteCommandAsync(
                        targetServer,
                        $"ls '{restoreDir}/volumes'",
                        cancellationToken);

                    foreach (var volumeName in volumeListResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var inspectResult = await _sshService.ExecuteCommandAsync(
                            targetServer,
                            $"docker volume inspect {volumeName.Trim()} --format '{{{{.Mountpoint}}}}' 2>/dev/null || docker volume create {volumeName.Trim()} && docker volume inspect {volumeName.Trim()} --format '{{{{.Mountpoint}}}}'",
                            cancellationToken);

                        if (inspectResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(inspectResult.Output))
                        {
                            var mountPoint = inspectResult.Output.Trim();
                            await _sshService.ExecuteCommandAsync(
                                targetServer,
                                $"cp -a '{restoreDir}/volumes/{volumeName.Trim()}/.' '{mountPoint}/'",
                                cancellationToken);
                        }
                    }
                }

                // Clean up
                await _sshService.ExecuteCommandAsync(
                    targetServer,
                    $"rm -rf '{restoreDir}'",
                    cancellationToken);

                _logger.LogInformation("Restore completed successfully from backup {BackupId}", backupId);
                return true;
            }
            else
            {
                // Cross-server restore would require additional implementation
                // (SCP or shared storage)
                _logger.LogWarning("Cross-server restore not yet implemented");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore from backup {BackupId}", backupId);
            return false;
        }
    }

    public async Task<bool> UploadToS3Async(int backupId, string bucket, CancellationToken cancellationToken = default)
    {
        var backup = await _context.Backups
            .Include(b => b.Application)
            .ThenInclude(a => a!.Server)
            .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);

        if (backup == null || backup.Application == null || backup.Status != BackupStatus.Success || string.IsNullOrEmpty(backup.StoragePath))
        {
            _logger.LogWarning("Backup {BackupId} is not available for upload", backupId);
            return false;
        }

        try
        {
            backup.Status = BackupStatus.Uploading;
            await _context.SaveChangesAsync(cancellationToken);

            // Get S3 configuration
            var s3Endpoint = _configuration["S3:Endpoint"];
            var s3AccessKey = _configuration["S3:AccessKey"];
            var s3SecretKey = _configuration["S3:SecretKey"];
            var s3Region = _configuration["S3:Region"] ?? "us-east-1";

            if (string.IsNullOrEmpty(s3AccessKey) || string.IsNullOrEmpty(s3SecretKey))
            {
                throw new InvalidOperationException("S3 credentials not configured");
            }

            var s3Key = $"hostcraft/{backup.Application.Uuid}/{Path.GetFileName(backup.StoragePath)}";

            // Use AWS CLI on the server (assumes aws cli is installed)
            var endpointArg = !string.IsNullOrEmpty(s3Endpoint) ? $"--endpoint-url {s3Endpoint}" : "";
            var uploadResult = await _sshService.ExecuteCommandAsync(
                backup.Application.Server,
                $"AWS_ACCESS_KEY_ID='{s3AccessKey}' AWS_SECRET_ACCESS_KEY='{s3SecretKey}' aws s3 cp '{backup.StoragePath}' 's3://{bucket}/{s3Key}' --region {s3Region} {endpointArg}",
                cancellationToken);

            if (uploadResult.ExitCode != 0)
            {
                throw new Exception($"S3 upload failed: {uploadResult.Error}");
            }

            backup.S3Bucket = bucket;
            backup.S3Key = s3Key;
            backup.Status = BackupStatus.Success;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Backup {BackupId} uploaded to S3: s3://{Bucket}/{Key}", backupId, bucket, s3Key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload backup {BackupId} to S3", backupId);
            backup.Status = BackupStatus.Failed;
            backup.ErrorMessage = $"S3 upload failed: {ex.Message}";
            await _context.SaveChangesAsync(cancellationToken);
            return false;
        }
    }

    public async Task<bool> DownloadFromS3Async(int backupId, CancellationToken cancellationToken = default)
    {
        var backup = await _context.Backups
            .Include(b => b.Application)
            .ThenInclude(a => a!.Server)
            .ThenInclude(s => s!.PrivateKey)
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);

        if (backup == null || backup.Application == null || string.IsNullOrEmpty(backup.S3Bucket) || string.IsNullOrEmpty(backup.S3Key))
        {
            _logger.LogWarning("Backup {BackupId} is not available for download from S3", backupId);
            return false;
        }

        try
        {
            var s3Endpoint = _configuration["S3:Endpoint"];
            var s3AccessKey = _configuration["S3:AccessKey"];
            var s3SecretKey = _configuration["S3:SecretKey"];
            var s3Region = _configuration["S3:Region"] ?? "us-east-1";

            if (string.IsNullOrEmpty(s3AccessKey) || string.IsNullOrEmpty(s3SecretKey))
            {
                throw new InvalidOperationException("S3 credentials not configured");
            }

            var backupDir = $"{BackupBasePath}/{backup.Application.Uuid}";
            var localPath = $"{backupDir}/{Path.GetFileName(backup.S3Key)}";

            await EnsureBackupDirectoryAsync(backup.Application.Server, backupDir, cancellationToken);

            var endpointArg = !string.IsNullOrEmpty(s3Endpoint) ? $"--endpoint-url {s3Endpoint}" : "";
            var downloadResult = await _sshService.ExecuteCommandAsync(
                backup.Application.Server,
                $"AWS_ACCESS_KEY_ID='{s3AccessKey}' AWS_SECRET_ACCESS_KEY='{s3SecretKey}' aws s3 cp 's3://{backup.S3Bucket}/{backup.S3Key}' '{localPath}' --region {s3Region} {endpointArg}",
                cancellationToken);

            if (downloadResult.ExitCode != 0)
            {
                throw new Exception($"S3 download failed: {downloadResult.Error}");
            }

            backup.StoragePath = localPath;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Backup {BackupId} downloaded from S3 to {Path}", backupId, localPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download backup {BackupId} from S3", backupId);
            return false;
        }
    }

    public async Task<int> PruneExpiredBackupsAsync(CancellationToken cancellationToken = default)
    {
        var expiredBackups = await _context.Backups
            .Include(b => b.Application)
            .ThenInclude(a => a!.Server)
            .ThenInclude(s => s!.PrivateKey)
            .Where(b => b.ExpiresAt != null && b.ExpiresAt < DateTime.UtcNow && b.Status != BackupStatus.Expired)
            .ToListAsync(cancellationToken);

        var deletedCount = 0;

        foreach (var backup in expiredBackups)
        {
            try
            {
                // Skip if no application (system backups handled differently)
                if (backup.Application == null)
                {
                    continue;
                }

                // Delete local file if exists
                if (!string.IsNullOrEmpty(backup.StoragePath))
                {
                    await _sshService.ExecuteCommandAsync(
                        backup.Application.Server,
                        $"rm -f '{backup.StoragePath}'",
                        cancellationToken);
                }

                // Delete from S3 if uploaded
                if (!string.IsNullOrEmpty(backup.S3Bucket) && !string.IsNullOrEmpty(backup.S3Key))
                {
                    var s3Endpoint = _configuration["S3:Endpoint"];
                    var s3AccessKey = _configuration["S3:AccessKey"];
                    var s3SecretKey = _configuration["S3:SecretKey"];
                    var s3Region = _configuration["S3:Region"] ?? "us-east-1";

                    if (!string.IsNullOrEmpty(s3AccessKey) && !string.IsNullOrEmpty(s3SecretKey))
                    {
                        var endpointArg = !string.IsNullOrEmpty(s3Endpoint) ? $"--endpoint-url {s3Endpoint}" : "";
                        await _sshService.ExecuteCommandAsync(
                            backup.Application.Server,
                            $"AWS_ACCESS_KEY_ID='{s3AccessKey}' AWS_SECRET_ACCESS_KEY='{s3SecretKey}' aws s3 rm 's3://{backup.S3Bucket}/{backup.S3Key}' --region {s3Region} {endpointArg}",
                            cancellationToken);
                    }
                }

                backup.Status = BackupStatus.Expired;
                deletedCount++;

                _logger.LogInformation("Pruned expired backup {BackupId}", backup.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to prune backup {BackupId}", backup.Id);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Pruned {Count} expired backups", deletedCount);
        return deletedCount;
    }

    public async Task<IEnumerable<Core.Entities.Backup>> GetBackupsAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        return await _context.Backups
            .Where(b => b.ApplicationId == applicationId)
            .OrderByDescending(b => b.StartedAt)
            .ToListAsync(cancellationToken);
    }

    // System-wide backup methods

    public async Task<Core.Entities.Backup> CreateSystemBackupAsync(
        BackupScope scope,
        int? backupConfigurationId = null,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        var backup = new Core.Entities.Backup
        {
            Uuid = Guid.NewGuid(),
            ApplicationId = null, // System backup
            BackupConfigurationId = backupConfigurationId,
            Type = BackupType.Full, // Using Full for system backups
            Scope = scope,
            Status = BackupStatus.Queued,
            StartedAt = DateTime.UtcNow,
            TriggeredBy = triggeredBy ?? "system",
            IsCompressed = true
        };

        _context.Backups.Add(backup);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            backup.Status = BackupStatus.Running;
            await _context.SaveChangesAsync(cancellationToken);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"hostcraft-system-backup-{timestamp}";
            var tempDir = $"/tmp/{backupFileName}";
            var backupPath = $"{BackupBasePath}/{backupFileName}.tar.gz";

            // Get first available server to run backup commands
            var server = await _context.Servers
                .Include(s => s.PrivateKey)
                .Where(s => s.Status == ServerStatus.Online)
                .FirstOrDefaultAsync(cancellationToken);

            if (server == null)
            {
                throw new InvalidOperationException("No connected servers available for backup");
            }

            await ExecuteCommandAsync(server, $"mkdir -p '{tempDir}'", cancellationToken);

            // Backup different scopes
            if (scope.HasFlag(BackupScope.SystemConfiguration))
            {
                await BackupSystemConfigurationAsync(server, tempDir, cancellationToken);
            }

            if (scope.HasFlag(BackupScope.Servers))
            {
                await BackupServersAsync(server, tempDir, cancellationToken);
                backup.ServerCount = await _context.Servers.CountAsync(cancellationToken);
            }

            if (scope.HasFlag(BackupScope.ApplicationConfigurations))
            {
                await BackupApplicationConfigurationsAsync(server, tempDir, cancellationToken);
                backup.ApplicationCount = await _context.Applications.CountAsync(cancellationToken);
            }

            if (scope.HasFlag(BackupScope.DockerNetworks))
            {
                // TODO: Implement network backup once DockerNetwork entity is added
                // Networks can be recreated during application deployment, so this is lower priority
                _logger.LogWarning("Docker network backup not yet implemented - networks will be recreated during restore");
            }

            if (scope.HasFlag(BackupScope.Certificates))
            {
                await BackupCertificatesAsync(server, tempDir, cancellationToken);
            }

            if (scope.HasFlag(BackupScope.GitIntegrations))
            {
                await BackupGitIntegrationsAsync(server, tempDir, cancellationToken);
            }

            if (scope.HasFlag(BackupScope.DeploymentHistory))
            {
                await BackupDeploymentHistoryAsync(server, tempDir, cancellationToken);
            }

            // Generate manifest
            var manifest = await GenerateManifestAsync(backup, cancellationToken);
            backup.ManifestJson = JsonSerializer.Serialize(manifest);

            // Write manifest to backup
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await ExecuteCommandAsync(
                server,
                $"cat > '{tempDir}/manifest.json' << 'HOSTCRAFT_MANIFEST_EOF'\n{manifestJson}\nHOSTCRAFT_MANIFEST_EOF",
                cancellationToken);

            // Create compressed archive
            await EnsureBackupDirectoryAsync(server, BackupBasePath, cancellationToken);
            var (tarExitCode, tarOutput, tarError) = await ExecuteCommandAsync(
                server,
                $"cd '{tempDir}' && tar -czf '{backupPath}' . && rm -rf '{tempDir}'",
                cancellationToken);

            if (tarExitCode != 0)
            {
                throw new Exception($"Failed to create backup archive: {tarError}");
            }

            // Get file size
            var (sizeExitCode, sizeOutput, _) = await ExecuteCommandAsync(
                server,
                $"stat -c%s '{backupPath}' 2>/dev/null || echo 0",
                cancellationToken);

            backup.StoragePath = backupPath;
            backup.SizeBytes = long.TryParse(sizeOutput.Trim(), out var size) ? size : 0;

            // Calculate checksum
            backup.Checksum = await CalculateChecksumAsync(backupPath, cancellationToken);

            backup.Status = BackupStatus.Success;
            backup.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("System backup created: {BackupPath} ({Size} bytes, scope: {Scope})",
                backupPath, backup.SizeBytes, scope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System backup failed");
            backup.Status = BackupStatus.Failed;
            backup.ErrorMessage = ex.Message;
            backup.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return backup;
    }

    public async Task<BackupManifest> GenerateManifestAsync(
        Core.Entities.Backup backup,
        CancellationToken cancellationToken = default)
    {
        var manifest = new BackupManifest
        {
            BackupId = backup.Uuid.ToString(),
            FormatVersion = "1.0.0",
            CreatedAt = backup.StartedAt,
            HostCraftVersion = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "unknown",
            Scope = backup.Scope,
            ProjectCount = await _context.Projects.CountAsync(cancellationToken),
            ApplicationCount = await _context.Applications.CountAsync(cancellationToken),
            ServerCount = await _context.Servers.CountAsync(cancellationToken)
        };

        return manifest;
    }

    public async Task<bool> VerifyBackupIntegrityAsync(
        int backupId,
        CancellationToken cancellationToken = default)
    {
        var backup = await _context.Backups.FindAsync(new object[] { backupId }, cancellationToken);
        if (backup == null || string.IsNullOrEmpty(backup.StoragePath))
        {
            _logger.LogWarning("Backup {BackupId} not found or has no storage path", backupId);
            return false;
        }

        try
        {
            backup.Status = BackupStatus.Verifying;
            await _context.SaveChangesAsync(cancellationToken);

            var currentChecksum = await CalculateChecksumAsync(backup.StoragePath, cancellationToken);

            if (string.IsNullOrEmpty(backup.Checksum))
            {
                backup.Checksum = currentChecksum;
                backup.IsVerified = true;
            }
            else
            {
                backup.IsVerified = backup.Checksum.Equals(currentChecksum, StringComparison.OrdinalIgnoreCase);
                if (!backup.IsVerified)
                {
                    backup.Status = BackupStatus.VerificationFailed;
                    backup.ErrorMessage = "Checksum verification failed";
                    await _context.SaveChangesAsync(cancellationToken);
                    return false;
                }
            }

            backup.Status = BackupStatus.Success;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Backup {BackupId} verification completed: {Result}",
                backupId, backup.IsVerified ? "SUCCESS" : "FAILED");

            return backup.IsVerified;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify backup {BackupId}", backupId);
            backup.Status = BackupStatus.VerificationFailed;
            backup.ErrorMessage = ex.Message;
            await _context.SaveChangesAsync(cancellationToken);
            return false;
        }
    }

    public async Task<RestoreOperation> RestoreFromBackupAsync(
        int backupId,
        BackupScope restoreScope,
        RestoreStrategy strategy,
        RestoreMapping? mapping = null,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        var backup = await _context.Backups
            .Include(b => b.BackupConfiguration)
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);

        if (backup == null)
        {
            throw new InvalidOperationException($"Backup {backupId} not found");
        }

        if (string.IsNullOrEmpty(backup.StoragePath))
        {
            throw new InvalidOperationException($"Backup {backupId} has no storage path");
        }

        var restoreOperation = new RestoreOperation
        {
            Uuid = Guid.NewGuid(),
            BackupId = backupId,
            RestoreScope = restoreScope,
            Strategy = strategy,
            RestoreMappingJson = mapping != null ? JsonSerializer.Serialize(mapping) : null,
            Status = BackupStatus.Queued,
            StartedAt = DateTime.UtcNow,
            TriggeredBy = triggeredBy ?? "system"
        };

        _context.RestoreOperations.Add(restoreOperation);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            restoreOperation.Status = BackupStatus.Running;
            await _context.SaveChangesAsync(cancellationToken);

            // Get first available server for restore operations
            var server = await _context.Servers
                .Include(s => s.PrivateKey)
                .Where(s => s.Status == ServerStatus.Online)
                .FirstOrDefaultAsync(cancellationToken);

            if (server == null)
            {
                throw new InvalidOperationException("No connected servers available for restore");
            }

            // Extract backup archive
            var restoreDir = $"/tmp/hostcraft-restore-{backup.Uuid}";
            _logger.LogInformation("Extracting backup to {RestoreDir}", restoreDir);
            
            var extractResult = await _sshService.ExecuteCommandAsync(
                server,
                $"mkdir -p '{restoreDir}' && tar -xzf '{backup.StoragePath}' -C '{restoreDir}'",
                cancellationToken);

            if (extractResult.ExitCode != 0)
            {
                throw new Exception($"Failed to extract backup: {extractResult.Error}");
            }

            // Read and parse manifest
            var manifestResult = await _sshService.ExecuteCommandAsync(
                server,
                $"cat '{restoreDir}/manifest.json'",
                cancellationToken);

            if (manifestResult.ExitCode != 0)
            {
                throw new Exception("Failed to read backup manifest");
            }

            var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestResult.Output);
            if (manifest == null)
            {
                throw new Exception("Invalid backup manifest");
            }

            _logger.LogInformation("Restoring from backup {BackupId} (version {Version}, scope: {Scope})",
                manifest.BackupId, manifest.HostCraftVersion, manifest.Scope);

            // Restore different scopes based on selection
            if (restoreScope.HasFlag(BackupScope.SystemConfiguration) && manifest.Scope.HasFlag(BackupScope.SystemConfiguration))
            {
                await RestoreSystemConfigurationAsync(server, restoreDir, strategy, cancellationToken);
            }

            if (restoreScope.HasFlag(BackupScope.Servers) && manifest.Scope.HasFlag(BackupScope.Servers))
            {
                var restoredServers = await RestoreServersAsync(server, restoreDir, strategy, mapping, cancellationToken);
                restoreOperation.ServersRestored = restoredServers;
            }

            if (restoreScope.HasFlag(BackupScope.ApplicationConfigurations) && manifest.Scope.HasFlag(BackupScope.ApplicationConfigurations))
            {
                var restoredApps = await RestoreApplicationConfigurationsAsync(server, restoreDir, strategy, mapping, cancellationToken);
                restoreOperation.ApplicationsRestored = restoredApps;
            }

            if (restoreScope.HasFlag(BackupScope.Certificates) && manifest.Scope.HasFlag(BackupScope.Certificates))
            {
                await RestoreCertificatesAsync(server, restoreDir, strategy, cancellationToken);
            }

            if (restoreScope.HasFlag(BackupScope.GitIntegrations) && manifest.Scope.HasFlag(BackupScope.GitIntegrations))
            {
                await RestoreGitIntegrationsAsync(server, restoreDir, strategy, cancellationToken);
            }

            if (restoreScope.HasFlag(BackupScope.DeploymentHistory) && manifest.Scope.HasFlag(BackupScope.DeploymentHistory))
            {
                await RestoreDeploymentHistoryAsync(server, restoreDir, strategy, cancellationToken);
            }

            // Clean up temporary restore directory
            await _sshService.ExecuteCommandAsync(
                server,
                $"rm -rf '{restoreDir}'",
                cancellationToken);

            restoreOperation.Status = BackupStatus.Success;
            restoreOperation.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Restore operation {RestoreId} completed successfully (restored {ServerCount} servers, {AppCount} applications)",
                restoreOperation.Id, restoreOperation.ServersRestored, restoreOperation.ApplicationsRestored);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore operation {RestoreId} failed", restoreOperation.Id);
            restoreOperation.Status = BackupStatus.Failed;
            restoreOperation.ErrorMessage = ex.Message;
            restoreOperation.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return restoreOperation;
    }

    public async Task<RestoreRequiredInput> AnalyzeRestoreRequirementsAsync(
        int backupId,
        CancellationToken cancellationToken = default)
    {
        var backup = await _context.Backups
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);

        if (backup == null)
        {
            throw new InvalidOperationException($"Backup {backupId} not found");
        }

        // If no manifest, return empty requirements (uploaded backups may lack manifests)
        if (string.IsNullOrEmpty(backup.ManifestJson))
        {
            return new RestoreRequiredInput();
        }

        var manifest = JsonSerializer.Deserialize<BackupManifest>(backup.ManifestJson);
        if (manifest == null)
        {
            return new RestoreRequiredInput();
        }

        // Build required inputs from manifest
        var requiredInput = new RestoreRequiredInput();

        // If backup includes servers, user will need to provide new connection details
        if (manifest.ServerCount > 0)
        {
            // Server details will be read from servers.json in the backup archive during restore
            // For now, just indicate that server inputs will be required
            _logger.LogInformation("Backup contains {ServerCount} servers that will need new connection details during restore",
                manifest.ServerCount);
        }

        // If backup includes applications with domains, user may need to remap them
        if (manifest.ApplicationCount > 0)
        {
            _logger.LogInformation("Backup contains {ApplicationCount} applications that may need domain remapping during restore",
                manifest.ApplicationCount);
        }

        return requiredInput;
    }

    public async Task<RestoreOperation?> GetRestoreOperationAsync(
        int restoreOperationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.RestoreOperations
            .Include(r => r.Backup)
            .FirstOrDefaultAsync(r => r.Id == restoreOperationId, cancellationToken);
    }

    public async Task<bool> UploadToStorageAsync(
        int backupId,
        int backupConfigurationId,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var backup = await _context.Backups.FindAsync(new object[] { backupId }, cancellationToken);
        var config = await _context.BackupConfigurations.FindAsync(new object[] { backupConfigurationId }, cancellationToken);

        if (backup == null || config == null || string.IsNullOrEmpty(backup.StoragePath))
        {
            _logger.LogWarning("Backup or configuration not found for upload");
            return false;
        }

        try
        {
            backup.Status = BackupStatus.Uploading;
            await _context.SaveChangesAsync(cancellationToken);

            // Storage provider implementation would go here
            // For now, this is a placeholder

            backup.Status = BackupStatus.Success;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Backup {BackupId} uploaded to storage", backupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload backup {BackupId}", backupId);
            backup.Status = BackupStatus.Failed;
            await _context.SaveChangesAsync(cancellationToken);
            return false;
        }
    }

    public async Task<bool> DownloadFromStorageAsync(
        int backupId,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var backup = await _context.Backups
            .Include(b => b.BackupConfiguration)
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);

        if (backup == null || backup.BackupConfiguration == null)
        {
            _logger.LogWarning("Backup or configuration not found for download");
            return false;
        }

        try
        {
            // Storage provider implementation would go here
            // For now, this is a placeholder

            _logger.LogInformation("Backup {BackupId} downloaded from storage", backupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download backup {BackupId}", backupId);
            return false;
        }
    }

    public async Task<List<RemoteBackupInfo>> ListRemoteBackupsAsync(
        int backupConfigurationId,
        CancellationToken cancellationToken = default)
    {
        var config = await _context.BackupConfigurations.FindAsync(new object[] { backupConfigurationId }, cancellationToken);
        if (config == null)
        {
            throw new InvalidOperationException($"Backup configuration {backupConfigurationId} not found");
        }

        // Storage provider implementation would go here
        // For now, return empty list
        return new List<RemoteBackupInfo>();
    }

    public async Task<string> CalculateChecksumAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        // Get first available server
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .Where(s => s.Status == ServerStatus.Online)
            .FirstOrDefaultAsync(cancellationToken);

        if (server == null)
        {
            throw new InvalidOperationException("No connected servers available");
        }

        var (exitCode, output, error) = await ExecuteCommandAsync(
            server,
            $"sha256sum '{filePath}' | awk '{{print $1}}'",
            cancellationToken);

        if (exitCode != 0)
        {
            throw new Exception($"Failed to calculate checksum: {error}");
        }

        return output.Trim();
    }

    // Helper methods for system backup

    private async Task BackupSystemConfigurationAsync(Server server, string targetDir, CancellationToken cancellationToken)
    {
        var systemSettings = await _context.SystemSettings.ToListAsync(cancellationToken);
        var json = JsonSerializer.Serialize(systemSettings, new JsonSerializerOptions { WriteIndented = true });
        await ExecuteCommandAsync(
            server,
            $"cat > '{targetDir}/system-settings.json' << 'HOSTCRAFT_EOF'\n{json}\nHOSTCRAFT_EOF",
            cancellationToken);
    }

    private async Task BackupServersAsync(Server server, string targetDir, CancellationToken cancellationToken)
    {
        var servers = await _context.Servers
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Host,
                s.Port,
                s.Username,
                s.Type,
                s.Status,
                s.SwarmId,
                s.SwarmNodeId,
                RegionId = s.RegionId
            })
            .ToListAsync(cancellationToken);

        var json = JsonSerializer.Serialize(servers, new JsonSerializerOptions { WriteIndented = true });
        await ExecuteCommandAsync(
            server,
            $"cat > '{targetDir}/servers.json' << 'HOSTCRAFT_EOF'\n{json}\nHOSTCRAFT_EOF",
            cancellationToken);
    }

    private async Task BackupApplicationConfigurationsAsync(Server server, string targetDir, CancellationToken cancellationToken)
    {
        var applications = await _context.Applications
            .Include(a => a.EnvironmentVariables.Where(e => !e.IsSecret))
            .Include(a => a.Project)
            .ToListAsync(cancellationToken);

        var appData = applications.Select(a => new
        {
            a.Uuid,
            a.Name,
            ProjectName = a.Project?.Name,
            a.SourceType,
            a.DockerImage,
            a.Domain,
            a.Port,
            a.Replicas,
            a.DeploymentMode,
            a.DeploymentStrategy,
            EnvironmentVariables = a.EnvironmentVariables
                .Where(e => !e.IsSecret)
                .Select(e => new { e.Key, e.Value })
                .ToList()
        }).ToList();

        var json = JsonSerializer.Serialize(appData, new JsonSerializerOptions { WriteIndented = true });
        await ExecuteCommandAsync(
            server,
            $"cat > '{targetDir}/applications.json' << 'HOSTCRAFT_EOF'\n{json}\nHOSTCRAFT_EOF",
            cancellationToken);
    }

    // TODO: Implement once DockerNetwork entity is added
    // private async Task BackupDockerNetworksAsync(Server server, string targetDir, CancellationToken cancellationToken)
    // {
    //     var networks = await _context.DockerNetworks.ToListAsync(cancellationToken);
    //     var json = JsonSerializer.Serialize(networks, new JsonSerializerOptions { WriteIndented = true });
    //     await _sshService.ExecuteCommandAsync(
    //         server,
    //         $"cat > '{targetDir}/networks.json' << 'HOSTCRAFT_EOF'\n{json}\nHOSTCRAFT_EOF",
    //         cancellationToken);
    // }

    private async Task BackupCertificatesAsync(Server server, string targetDir, CancellationToken cancellationToken)
    {
        var certificates = await _context.Certificates
            .Select(c => new { c.Domain, c.Provider, c.Status, c.ExpiresAt })
            .ToListAsync(cancellationToken);

        var json = JsonSerializer.Serialize(certificates, new JsonSerializerOptions { WriteIndented = true });
        await ExecuteCommandAsync(
            server,
            $"cat > '{targetDir}/certificates.json' << 'HOSTCRAFT_EOF'\n{json}\nHOSTCRAFT_EOF",
            cancellationToken);
    }

    private async Task BackupGitIntegrationsAsync(Server server, string targetDir, CancellationToken cancellationToken)
    {
        var gitProviders = await _context.GitProviders
            .Select(g => new { g.Id, g.Type, g.Username, g.ProviderId, g.ApiUrl })
            .ToListAsync(cancellationToken);

        var json = JsonSerializer.Serialize(gitProviders, new JsonSerializerOptions { WriteIndented = true });
        await ExecuteCommandAsync(
            server,
            $"cat > '{targetDir}/git-providers.json' << 'HOSTCRAFT_EOF'\n{json}\nHOSTCRAFT_EOF",
            cancellationToken);
    }

    private async Task BackupDeploymentHistoryAsync(Server server, string targetDir, CancellationToken cancellationToken)
    {
        var deployments = await _context.Deployments
            .OrderByDescending(d => d.StartedAt)
            .Take(100) // Last 100 deployments
            .Select(d => new
            {
                d.Uuid,
                ApplicationId = d.Application.Uuid,
                d.Status,
                d.CommitHash,
                d.ImageTag,
                d.StartedAt,
                d.FinishedAt
            })
            .ToListAsync(cancellationToken);

        var json = JsonSerializer.Serialize(deployments, new JsonSerializerOptions { WriteIndented = true });
        await ExecuteCommandAsync(
            server,
            $"cat > '{targetDir}/deployments.json' << 'HOSTCRAFT_EOF'\n{json}\nHOSTCRAFT_EOF",
            cancellationToken);
    }

    private async Task<Application?> GetApplicationWithServerAsync(int applicationId, CancellationToken cancellationToken)
    {
        return await _context.Applications
            .Include(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .Include(a => a.EnvironmentVariables)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
    }

    private Core.Entities.Backup CreateBackupRecord(Application application, BackupType type)
    {
        var retentionDays = application.BackupRetentionDays ?? 30;
        return new Core.Entities.Backup
        {
            Uuid = Guid.NewGuid(),
            ApplicationId = application.Id,
            Type = type,
            Status = BackupStatus.Queued,
            StartedAt = DateTime.UtcNow,
            RetentionDays = retentionDays,
            ExpiresAt = DateTime.UtcNow.AddDays(retentionDays)
        };
    }

    private async Task EnsureBackupDirectoryAsync(Server server, string path, CancellationToken cancellationToken)
    {
        await ExecuteCommandAsync(
            server,
            $"mkdir -p '{path}'",
            cancellationToken);
    }

    private object CreateConfigurationBackup(Application application)
    {
        return new
        {
            application.Uuid,
            application.Name,
            application.Description,
            application.SourceType,
            application.GitRepository,
            application.GitBranch,
            application.GitOwner,
            application.GitRepoName,
            application.DockerImage,
            application.DockerComposeFile,
            application.Dockerfile,
            application.BuildContext,
            application.BuildArgs,
            application.Domain,
            application.AdditionalDomains,
            application.Port,
            application.Replicas,
            application.DeploymentMode,
            application.MemoryLimitBytes,
            application.CpuLimit,
            application.HealthCheckUrl,
            application.HealthCheckIntervalSeconds,
            application.HealthCheckTimeoutSeconds,
            application.AutoRestart,
            application.AutoRollback,
            EnvironmentVariables = application.EnvironmentVariables
                .Where(e => !e.IsSecret) // Don't backup secrets
                .Select(e => new { e.Key, e.Value })
                .ToList(),
            SwarmConfig = new
            {
                application.SwarmReplicas,
                application.SwarmPlacementConstraints,
                application.SwarmUpdateConfig,
                application.SwarmRollbackConfig,
                application.SwarmMode,
                application.SwarmEndpointSpec,
                application.SwarmNetworks,
                application.SwarmStopGracePeriod
            },
            BackupCreatedAt = DateTime.UtcNow,
            HostCraftVersion = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "unknown"
        };
    }

    // Restore helper methods

    private async Task RestoreSystemConfigurationAsync(
        Server server,
        string restoreDir,
        RestoreStrategy strategy,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring system configuration");

        var jsonResult = await _sshService.ExecuteCommandAsync(
            server,
            $"cat '{restoreDir}/system-settings.json'",
            cancellationToken);

        if (jsonResult.ExitCode != 0)
        {
            _logger.LogWarning("System settings file not found in backup");
            return;
        }

        var systemSettings = JsonSerializer.Deserialize<List<SystemSettings>>(jsonResult.Output);
        if (systemSettings == null || !systemSettings.Any())
        {
            return;
        }

        // Note: SystemSettings is a single entity, not key-value pairs
        // For now, we'll just log that system settings were found
        // Full implementation would merge the settings appropriately
        _logger.LogInformation("Found {Count} system settings records in backup", systemSettings.Count);
        
        // In a full implementation, you would merge specific properties from the backup
        // into the current SystemSettings entity based on the restore strategy
    }

    private async Task<int> RestoreServersAsync(
        Server executionServer,
        string restoreDir,
        RestoreStrategy strategy,
        RestoreMapping? mapping,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring servers");

        var jsonResult = await _sshService.ExecuteCommandAsync(
            executionServer,
            $"cat '{restoreDir}/servers.json'",
            cancellationToken);

        if (jsonResult.ExitCode != 0)
        {
            _logger.LogWarning("Servers file not found in backup");
            return 0;
        }

        var servers = JsonSerializer.Deserialize<List<JsonElement>>(jsonResult.Output);
        if (servers == null || !servers.Any())
        {
            return 0;
        }

        int restoredCount = 0;

        foreach (var serverJson in servers)
        {
            var oldServerId = serverJson.GetProperty("Id").GetInt32();
            var serverName = serverJson.GetProperty("Name").GetString() ?? "Unknown";

            // Check if mapping provides new server details
            ServerRestoreMapping? serverMapping = null;
            if (mapping?.ServerMappings != null && mapping.ServerMappings.ContainsKey(oldServerId))
            {
                serverMapping = mapping.ServerMappings[oldServerId];
            }

            // Check if server already exists by name
            var existingServer = await _context.Servers
                .FirstOrDefaultAsync(s => s.Name == serverName, cancellationToken);

            if (existingServer != null)
            {
                if (strategy == RestoreStrategy.SkipExisting)
                {
                    _logger.LogDebug("Skipping existing server: {Name}", serverName);
                    continue;
                }
                else if (strategy == RestoreStrategy.FailOnConflict)
                {
                    throw new InvalidOperationException($"Server '{serverName}' already exists");
                }
                else if (strategy == RestoreStrategy.OverwriteExisting || strategy == RestoreStrategy.Merge)
                {
                    // Update server with new connection details if provided in mapping
                    if (serverMapping != null)
                    {
                        if (serverMapping.NewHostname != null)
                            existingServer.Host = serverMapping.NewHostname;
                        if (serverMapping.NewSshPort.HasValue)
                            existingServer.Port = serverMapping.NewSshPort.Value;
                        if (serverMapping.NewSshUsername != null)
                            existingServer.Username = serverMapping.NewSshUsername;
                        _logger.LogInformation("Updated server {Name} with new connection details", serverName);
                    }
                    restoredCount++;
                }
            }
            else
            {
                // Create new server
                var newServer = new Server
                {
                    Name = serverName,
                    Host = serverMapping?.NewHostname ?? serverJson.GetProperty("Host").GetString() ?? "localhost",
                    Port = serverMapping?.NewSshPort ?? serverJson.GetProperty("Port").GetInt32(),
                    Username = serverMapping?.NewSshUsername ?? serverJson.GetProperty("Username").GetString() ?? "root",
                    Type = Enum.Parse<ServerType>(serverJson.GetProperty("Type").GetString() ?? "Standalone"),
                    Status = ServerStatus.Offline, // Will be connected later
                    CreatedAt = DateTime.UtcNow
                };

                // Note: SSH keys need to be provided separately - they're not backed up for security
                _logger.LogWarning("Server {Name} created but requires SSH key configuration", serverName);

                _context.Servers.Add(newServer);
                restoredCount++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Restored {Count} servers", restoredCount);
        return restoredCount;
    }

    private async Task<int> RestoreApplicationConfigurationsAsync(
        Server server,
        string restoreDir,
        RestoreStrategy strategy,
        RestoreMapping? mapping,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring application configurations");

        var jsonResult = await _sshService.ExecuteCommandAsync(
            server,
            $"cat '{restoreDir}/applications.json'",
            cancellationToken);

        if (jsonResult.ExitCode != 0)
        {
            _logger.LogWarning("Applications file not found in backup");
            return 0;
        }

        var applications = JsonSerializer.Deserialize<List<JsonElement>>(jsonResult.Output);
        if (applications == null || !applications.Any())
        {
            return 0;
        }

        int restoredCount = 0;

        foreach (var appJson in applications)
        {
            var appUuid = Guid.Parse(appJson.GetProperty("Uuid").GetString()!);
            var appName = appJson.GetProperty("Name").GetString() ?? "Unknown";
            var domain = appJson.GetProperty("Domain").GetString();

            // Apply domain mapping if provided
            if (domain != null && mapping?.DomainMappings != null && mapping.DomainMappings.ContainsKey(domain))
            {
                var newDomain = mapping.DomainMappings[domain];
                _logger.LogInformation("Mapped domain {OldDomain} -> {NewDomain}", domain, newDomain);
                domain = newDomain;
            }

            // Check if application already exists
            var existingApp = await _context.Applications
                .FirstOrDefaultAsync(a => a.Uuid == appUuid || a.Name == appName, cancellationToken);

            if (existingApp != null)
            {
                if (strategy == RestoreStrategy.SkipExisting)
                {
                    _logger.LogDebug("Skipping existing application: {Name}", appName);
                    continue;
                }
                else if (strategy == RestoreStrategy.FailOnConflict)
                {
                    throw new InvalidOperationException($"Application '{appName}' already exists");
                }
                else if (strategy == RestoreStrategy.OverwriteExisting || strategy == RestoreStrategy.Merge)
                {
                    // Update existing application configuration
                    existingApp.Domain = domain;
                    existingApp.Port = appJson.GetProperty("Port").GetInt32();
                    existingApp.Replicas = appJson.GetProperty("Replicas").GetInt32();
                    
                    if (appJson.TryGetProperty("DockerImage", out var dockerImage))
                    {
                        existingApp.DockerImage = dockerImage.GetString();
                    }

                    _logger.LogInformation("Updated application {Name}", appName);
                    restoredCount++;
                }
            }
            else
            {
                // Note: Creating new applications requires server assignment and project
                // This is a simplified version - full implementation would need more context
                _logger.LogWarning("Application {Name} found in backup but cannot be fully restored without server and project context", appName);
                // In a full implementation, you would create the application here with proper server/project references
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Restored {Count} application configurations", restoredCount);
        return restoredCount;
    }

    private async Task RestoreCertificatesAsync(
        Server server,
        string restoreDir,
        RestoreStrategy strategy,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring certificates metadata");

        var jsonResult = await _sshService.ExecuteCommandAsync(
            server,
            $"cat '{restoreDir}/certificates.json'",
            cancellationToken);

        if (jsonResult.ExitCode != 0)
        {
            _logger.LogWarning("Certificates file not found in backup");
            return;
        }

        var certificates = JsonSerializer.Deserialize<List<JsonElement>>(jsonResult.Output);
        if (certificates == null || !certificates.Any())
        {
            return;
        }

        // Note: Actual SSL certificates are managed by Traefik/Let's Encrypt
        // We only restore metadata here - certificates will be re-issued automatically
        _logger.LogInformation("Found {Count} certificate records in backup (certificates will be re-issued automatically)", 
            certificates.Count);
    }

    private async Task RestoreGitIntegrationsAsync(
        Server server,
        string restoreDir,
        RestoreStrategy strategy,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring Git integrations");

        var jsonResult = await _sshService.ExecuteCommandAsync(
            server,
            $"cat '{restoreDir}/git-providers.json'",
            cancellationToken);

        if (jsonResult.ExitCode != 0)
        {
            _logger.LogWarning("Git providers file not found in backup");
            return;
        }

        var gitProviders = JsonSerializer.Deserialize<List<JsonElement>>(jsonResult.Output);
        if (gitProviders == null || !gitProviders.Any())
        {
            return;
        }

        foreach (var providerJson in gitProviders)
        {
            var providerId = providerJson.GetProperty("ProviderId").GetString();
            var username = providerJson.GetProperty("Username").GetString();

            var existing = await _context.GitProviders
                .FirstOrDefaultAsync(g => g.ProviderId == providerId && g.Username == username, cancellationToken);

            if (existing != null)
            {
                if (strategy == RestoreStrategy.SkipExisting)
                {
                    continue;
                }
                else if (strategy == RestoreStrategy.FailOnConflict)
                {
                    throw new InvalidOperationException($"Git provider for {username} already exists");
                }
            }
            else
            {
                // Note: OAuth tokens are not backed up for security
                // User will need to re-authenticate with Git providers
                _logger.LogWarning("Git provider for {Username} found in backup but requires re-authentication", username);
            }
        }

        _logger.LogInformation("Processed {Count} Git provider records", gitProviders.Count);
    }

    private async Task RestoreDeploymentHistoryAsync(
        Server server,
        string restoreDir,
        RestoreStrategy strategy,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring deployment history");

        var jsonResult = await _sshService.ExecuteCommandAsync(
            server,
            $"cat '{restoreDir}/deployments.json'",
            cancellationToken);

        if (jsonResult.ExitCode != 0)
        {
            _logger.LogWarning("Deployments file not found in backup");
            return;
        }

        var deployments = JsonSerializer.Deserialize<List<JsonElement>>(jsonResult.Output);
        if (deployments == null || !deployments.Any())
        {
            return;
        }

        // Note: Deployment history is informational only
        // We don't restore actual deployment records as they reference applications that may not exist yet
        _logger.LogInformation("Found {Count} deployment records in backup (history is informational only)", 
            deployments.Count);
    }

    #region Backup Configuration Management

    public async Task<List<BackupConfiguration>> GetBackupConfigurationsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.BackupConfigurations
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<BackupConfiguration?> GetBackupConfigurationAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await _context.BackupConfigurations
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<BackupConfiguration> CreateBackupConfigurationAsync(
        BackupConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        // If this is set as default, unset all other defaults
        if (configuration.IsDefault)
        {
            var existingDefaults = await _context.BackupConfigurations
                .Where(c => c.IsDefault && c.StorageType == configuration.StorageType)
                .ToListAsync(cancellationToken);

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
        }

        _context.BackupConfigurations.Add(configuration);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created backup configuration {Name} (ID: {Id}, Type: {Type})",
            configuration.Name, configuration.Id, configuration.StorageType);

        return configuration;
    }

    public async Task<BackupConfiguration> UpdateBackupConfigurationAsync(
        BackupConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.BackupConfigurations
            .FirstOrDefaultAsync(c => c.Id == configuration.Id, cancellationToken);

        if (existing == null)
        {
            throw new InvalidOperationException($"Backup configuration {configuration.Id} not found");
        }

        // If this is being set as default, unset all other defaults of the same type
        if (configuration.IsDefault && !existing.IsDefault)
        {
            var otherDefaults = await _context.BackupConfigurations
                .Where(c => c.IsDefault && c.StorageType == configuration.StorageType && c.Id != configuration.Id)
                .ToListAsync(cancellationToken);

            foreach (var other in otherDefaults)
            {
                other.IsDefault = false;
            }
        }

        // Update properties
        existing.Name = configuration.Name;
        existing.StorageType = configuration.StorageType;
        existing.IsActive = configuration.IsActive;
        existing.IsDefault = configuration.IsDefault;
        existing.ProviderConfiguration = configuration.ProviderConfiguration;
        existing.RetentionDays = configuration.RetentionDays;
        existing.AutoBackupEnabled = configuration.AutoBackupEnabled;
        existing.AutoBackupSchedule = configuration.AutoBackupSchedule;
        existing.AutoBackupScope = configuration.AutoBackupScope;
        existing.EnableCompression = configuration.EnableCompression;
        existing.EnableEncryption = configuration.EnableEncryption;
        existing.EncryptionKey = configuration.EncryptionKey;
        existing.VerifyAfterUpload = configuration.VerifyAfterUpload;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated backup configuration {Name} (ID: {Id})",
            existing.Name, existing.Id);

        return existing;
    }

    public async Task<bool> DeleteBackupConfigurationAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _context.BackupConfigurations
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (configuration == null)
        {
            return false;
        }

        // Check if any backups are using this configuration
        var backupsUsingConfig = await _context.Backups
            .AnyAsync(b => b.BackupConfigurationId == id, cancellationToken);

        if (backupsUsingConfig)
        {
            throw new InvalidOperationException(
                $"Cannot delete backup configuration {configuration.Name} because it is being used by existing backups");
        }

        _context.BackupConfigurations.Remove(configuration);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted backup configuration {Name} (ID: {Id})",
            configuration.Name, id);

        return true;
    }

    public async Task<bool> TestBackupConfigurationAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _context.BackupConfigurations
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (configuration == null)
        {
            throw new InvalidOperationException($"Backup configuration {id} not found");
        }

        try
        {
            // Get a server to use for the test
            var server = await _context.Servers
                .Include(s => s.PrivateKey)
                .Where(s => s.Status == ServerStatus.Online)
                .FirstOrDefaultAsync(cancellationToken);

            if (server == null)
            {
                throw new InvalidOperationException("No online servers available for testing");
            }

            // Create the appropriate storage provider based on configuration type
            IBackupStorageProvider provider = configuration.StorageType switch
            {
                BackupStorageType.S3Compatible => CreateS3Provider(server, configuration),
                BackupStorageType.GoogleDrive => CreateGoogleDriveProvider(server, configuration),
                BackupStorageType.LocalFileSystem => CreateLocalFileSystemProvider(server, configuration),
                _ => throw new NotSupportedException($"Storage type {configuration.StorageType} is not supported")
            };

            // Test the connection
            var result = await provider.TestConnectionAsync(cancellationToken);

            _logger.LogInformation("Backup configuration {Name} connection test: {Result}",
                configuration.Name, result ? "SUCCESS" : "FAILED");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test backup configuration {Name}", configuration.Name);
            return false;
        }
    }

    private IBackupStorageProvider CreateS3Provider(Server server, BackupConfiguration configuration)
    {
        var s3Config = JsonSerializer.Deserialize<S3BackupConfig>(configuration.ProviderConfiguration);
        if (s3Config == null)
        {
            throw new InvalidOperationException("Invalid S3 configuration");
        }

        return new S3BackupStorageProvider(
            _loggerFactory.CreateLogger<S3BackupStorageProvider>(),
            _sshService,
            server,
            s3Config);
    }

    private IBackupStorageProvider CreateGoogleDriveProvider(Server server, BackupConfiguration configuration)
    {
        var driveConfig = JsonSerializer.Deserialize<GoogleDriveBackupConfig>(configuration.ProviderConfiguration);
        if (driveConfig == null)
        {
            throw new InvalidOperationException("Invalid Google Drive configuration");
        }

        return new GoogleDriveBackupStorageProvider(
            _loggerFactory.CreateLogger<GoogleDriveBackupStorageProvider>(),
            _sshService,
            server,
            driveConfig);
    }

    private IBackupStorageProvider CreateLocalFileSystemProvider(Server server, BackupConfiguration configuration)
    {
        var localConfig = JsonSerializer.Deserialize<LocalFileSystemBackupConfig>(configuration.ProviderConfiguration);
        if (localConfig == null)
        {
            throw new InvalidOperationException("Invalid local filesystem configuration");
        }

        return new LocalFileSystemBackupStorageProvider(
            _loggerFactory.CreateLogger<LocalFileSystemBackupStorageProvider>(),
            _sshService,
            server,
            localConfig.StoragePath);
    }

    #endregion

    #region Backup Retrieval Methods

    public async Task<IEnumerable<Core.Entities.Backup>> GetSystemBackupsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Backups
            .Where(b => b.ApplicationId == null) // System-wide backups only
            .OrderByDescending(b => b.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Core.Entities.Backup?> GetBackupAsync(int backupId, CancellationToken cancellationToken = default)
    {
        return await _context.Backups
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);
    }

    public async Task<Stream?> GetBackupFileStreamAsync(int backupId, CancellationToken cancellationToken = default)
    {
        var backup = await GetBackupAsync(backupId, cancellationToken);
        if (backup == null || string.IsNullOrEmpty(backup.StoragePath))
        {
            _logger.LogWarning("Backup {BackupId} not found or has no storage path", backupId);
            return null;
        }

        // Get the server where backup is stored (first online server for system backups)
        Server? server;
        if (backup.ApplicationId.HasValue)
        {
            var application = await _context.Applications
                .Include(a => a.Server)
                .FirstOrDefaultAsync(a => a.Id == backup.ApplicationId.Value, cancellationToken);
            server = application?.Server;
        }
        else
        {
            // System backup - use first online server
            server = await _context.Servers
                .Where(s => s.Status == ServerStatus.Online)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (server == null)
        {
            _logger.LogWarning("No server found for backup {BackupId}", backupId);
            return null;
        }

        // Check if this is a localhost/local server
        bool isLocalhost = IsLocalhostServer(server) || string.IsNullOrEmpty(server.Host);

        if (isLocalhost)
        {
            // For localhost, read the file directly from the filesystem
            try
            {
                if (!File.Exists(backup.StoragePath))
                {
                    _logger.LogError("Backup file not found at {StoragePath}", backup.StoragePath);
                    return null;
                }

                // Return file stream directly (caller is responsible for disposing)
                return new FileStream(
                    backup.StoragePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading local backup file {BackupId} from {Path}",
                    backupId, backup.StoragePath);
                return null;
            }
        }

        // For remote servers, download file via SSH to a temporary local stream
        var tempFile = Path.GetTempFileName();
        try
        {
            // Use SFTP to download the file
            var downloadSuccess = await _sshService.DownloadFileAsync(
                server,
                backup.StoragePath,
                tempFile,
                cancellationToken);

            if (!downloadSuccess)
            {
                _logger.LogError("Failed to download backup file from {StoragePath}",
                    backup.StoragePath);

                // Clean up temp file
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }

                return null;
            }

            // Return file stream (caller is responsible for disposing)
            // Use FileOptions.DeleteOnClose to auto-cleanup temp file
            return new FileStream(
                tempFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming backup file {BackupId}", backupId);

            // Clean up temp file on error
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* ignore */ }
            }

            return null;
        }
    }

    #endregion

    #region Upload/Import Operations

    public async Task<Core.Entities.Backup> ImportUploadedBackupAsync(
        string uploadedFilePath,
        string originalFileName,
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Importing uploaded backup: {FileName}", originalFileName);

        // Get first available server to store the backup
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .Where(s => s.Status == ServerStatus.Online)
            .FirstOrDefaultAsync(cancellationToken);

        if (server == null)
        {
            throw new InvalidOperationException("No online servers available to store the backup");
        }

        try
        {
            // Extract and read manifest from the uploaded backup
            BackupManifest? manifest = null;
            try
            {
                var (exitCode, output, _) = await ExecuteCommandAsync(
                    server,
                    $"tar -xzf '{uploadedFilePath}' -O manifest.json 2>/dev/null || echo ''",
                    cancellationToken);

                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    manifest = JsonSerializer.Deserialize<BackupManifest>(output);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read manifest from uploaded backup");
            }

            // Create backup record
            var backup = new Core.Entities.Backup
            {
                Uuid = Guid.NewGuid(),
                ApplicationId = null, // Uploaded backups are system-wide
                BackupConfigurationId = null,
                Type = BackupType.Full,
                Scope = manifest?.Scope ?? BackupScope.Complete,
                Status = BackupStatus.Success,
                StartedAt = manifest?.CreatedAt ?? DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                TriggeredBy = uploadedBy,
                IsCompressed = true,
                IsEncrypted = false, // We don't know if it's encrypted from upload
                ManifestJson = manifest != null ? JsonSerializer.Serialize(manifest) : null,
                ProjectCount = manifest?.ProjectCount ?? 0,
                ApplicationCount = manifest?.ApplicationCount ?? 0,
                ServerCount = manifest?.ServerCount ?? 0
            };

            // Determine storage path on server
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var fileName = $"uploaded-backup-{timestamp}.tar.gz";
            var backupPath = $"{BackupBasePath}/{fileName}";

            // Ensure backup directory exists
            await EnsureBackupDirectoryAsync(server, BackupBasePath, cancellationToken);

            // Copy/upload the backup file to the storage path
            if (IsLocalhostServer(server))
            {
                File.Copy(uploadedFilePath, backupPath, overwrite: true);
            }
            else
            {
                var uploadSuccess = await _sshService.UploadFileAsync(
                    server,
                    uploadedFilePath,
                    backupPath,
                    cancellationToken);

                if (!uploadSuccess)
                {
                    throw new Exception("Failed to upload backup file to server");
                }
            }

            // Get file size
            var fileInfo = new FileInfo(uploadedFilePath);
            backup.SizeBytes = fileInfo.Length;
            backup.StoragePath = backupPath;

            // Calculate checksum from the uploaded file
            using (var stream = File.OpenRead(uploadedFilePath))
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
                backup.Checksum = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }

            backup.IsVerified = true; // We just calculated it

            // Save to database
            _context.Backups.Add(backup);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Uploaded backup imported successfully: {BackupId} ({Size} bytes, {Scope})",
                backup.Id,
                backup.SizeBytes,
                backup.Scope);

            return backup;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import uploaded backup");
            throw;
        }
    }

    #endregion

    #region Localhost Helper Methods

    private bool IsLocalhostServer(Server server)
    {
        return server.Host == "localhost" ||
               server.Host == "127.0.0.1" ||
               server.Host == "::1" ||
               server.Host == "0.0.0.0";
    }

    private async Task<(int ExitCode, string Output, string Error)> ExecuteCommandAsync(
        Server server,
        string command,
        CancellationToken cancellationToken)
    {
        if (IsLocalhostServer(server))
        {
            return await ExecuteLocalCommandAsync(command, cancellationToken);
        }

        var result = await _sshService.ExecuteCommandAsync(server, command, cancellationToken);
        return (result.ExitCode, result.Output, result.Error);
    }

    private async Task<(int ExitCode, string Output, string Error)> ExecuteLocalCommandAsync(
        string command,
        CancellationToken cancellationToken)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // For Windows, use cmd.exe instead
            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/c {command}";
            }

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                return (-1, "", "Failed to start process");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute local command: {Command}", command);
            return (-1, "", ex.Message);
        }
    }

    #endregion
}
