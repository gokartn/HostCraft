using System.Text;
using System.Text.Json;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
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

    private const string BackupBasePath = "/var/hostcraft/backups";

    public BackupService(
        HostCraftDbContext context,
        ISshService sshService,
        IDockerService dockerService,
        IConfiguration configuration,
        ILogger<BackupService> logger)
    {
        _context = context;
        _sshService = sshService;
        _dockerService = dockerService;
        _configuration = configuration;
        _logger = logger;
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
            .ThenInclude(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);

        if (backup == null)
        {
            _logger.LogWarning("Backup {BackupId} not found for restore", backupId);
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
            .ThenInclude(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);

        if (backup == null || backup.Status != BackupStatus.Success || string.IsNullOrEmpty(backup.StoragePath))
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
            .ThenInclude(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(b => b.Id == backupId, cancellationToken);

        if (backup == null || string.IsNullOrEmpty(backup.S3Bucket) || string.IsNullOrEmpty(backup.S3Key))
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
            .ThenInclude(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .Where(b => b.ExpiresAt != null && b.ExpiresAt < DateTime.UtcNow && b.Status != BackupStatus.Expired)
            .ToListAsync(cancellationToken);

        var deletedCount = 0;

        foreach (var backup in expiredBackups)
        {
            try
            {
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
        await _sshService.ExecuteCommandAsync(
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
}
