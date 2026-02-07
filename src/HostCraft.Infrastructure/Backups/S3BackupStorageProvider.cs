using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Backups;

/// <summary>
/// S3-compatible storage provider for backups
/// Supports AWS S3, MinIO, DigitalOcean Spaces, Backblaze B2, and other S3-compatible services
/// </summary>
public class S3BackupStorageProvider : IBackupStorageProvider
{
    private readonly ILogger<S3BackupStorageProvider> _logger;
    private readonly ISshService _sshService;
    private readonly Server _server;
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _keyPrefix;

    public S3BackupStorageProvider(
        ILogger<S3BackupStorageProvider> logger,
        ISshService sshService,
        Server server,
        S3BackupConfig config)
    {
        _logger = logger;
        _sshService = sshService;
        _server = server;
        _bucketName = config.BucketName;
        _keyPrefix = config.Prefix ?? "";

        // Create S3 client with custom endpoint if provided (for MinIO, DigitalOcean Spaces, etc.)
        var s3Config = new AmazonS3Config
        {
            ForcePathStyle = config.UsePathStyle,
            UseHttp = !config.UseSsl,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2
        };

        // For custom endpoints (MinIO, etc.), set ServiceURL and do NOT set RegionEndpoint
        // For AWS S3, set RegionEndpoint
        if (!string.IsNullOrEmpty(config.Endpoint))
        {
            s3Config.ServiceURL = config.Endpoint;
            // For custom S3-compatible services, use a placeholder region (not used but required by SDK)
            // MinIO and others ignore this value
            s3Config.AuthenticationRegion = string.IsNullOrWhiteSpace(config.Region) ? "us-east-1" : config.Region;
        }
        else
        {
            // Standard AWS S3 - region is required and meaningful
            if (string.IsNullOrWhiteSpace(config.Region))
            {
                throw new ArgumentException("Region is required for AWS S3");
            }
            s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(config.Region);
        }

        _s3Client = new AmazonS3Client(config.AccessKeyId, config.SecretAccessKey, s3Config);
    }

    public async Task<string> UploadBackupAsync(
        string localBackupPath,
        BackupManifest manifest,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = Path.GetFileName(localBackupPath);
            var s3Key = string.IsNullOrEmpty(_keyPrefix) 
                ? fileName 
                : $"{_keyPrefix.TrimEnd('/')}/{fileName}";

            _logger.LogInformation("Uploading backup to S3: s3://{Bucket}/{Key}", _bucketName, s3Key);

            // Get file size for progress reporting
            var sizeResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"stat -c%s '{localBackupPath}' 2>/dev/null || echo 0",
                cancellationToken);

            var fileSize = long.TryParse(sizeResult.Output.Trim(), out var size) ? size : 0;

            // For large files (>100MB), use multipart upload
            if (fileSize > 100 * 1024 * 1024)
            {
                await UploadLargeFileAsync(localBackupPath, s3Key, fileSize, progress, cancellationToken);
            }
            else
            {
                await UploadSmallFileAsync(localBackupPath, s3Key, fileSize, progress, cancellationToken);
            }

            _logger.LogInformation("Backup uploaded successfully to S3: s3://{Bucket}/{Key}", _bucketName, s3Key);
            return s3Key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload backup to S3");
            throw;
        }
    }

    private async Task UploadSmallFileAsync(
        string localBackupPath,
        string s3Key,
        long fileSize,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Create a temporary local file to upload
        var tempFile = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid()}.tar.gz");

        try
        {
            progress?.Report(new BackupProgress(0, fileSize, Path.GetFileName(localBackupPath), "Downloading from server"));

            // Download file from server to local temp
            var scpResult = await _sshService.DownloadFileAsync(_server, localBackupPath, tempFile, cancellationToken);
            if (!scpResult)
            {
                throw new Exception("Failed to download backup from server");
            }

            progress?.Report(new BackupProgress(fileSize / 2, fileSize, Path.GetFileName(localBackupPath), "Uploading to S3"));

            // Upload to S3
            var putRequest = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = s3Key,
                FilePath = tempFile,
                ContentType = "application/gzip"
            };

            await _s3Client.PutObjectAsync(putRequest, cancellationToken);

            progress?.Report(new BackupProgress(fileSize, fileSize, Path.GetFileName(localBackupPath), "Upload complete"));
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private async Task UploadLargeFileAsync(
        string localBackupPath,
        string s3Key,
        long fileSize,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid()}.tar.gz");

        try
        {
            progress?.Report(new BackupProgress(0, fileSize, Path.GetFileName(localBackupPath), "Downloading from server"));

            // Download file from server to local temp
            var scpResult = await _sshService.DownloadFileAsync(_server, localBackupPath, tempFile, cancellationToken);
            if (!scpResult)
            {
                throw new Exception("Failed to download backup from server");
            }

            progress?.Report(new BackupProgress(fileSize / 2, fileSize, Path.GetFileName(localBackupPath), "Uploading to S3 (multipart)"));

            // Use TransferUtility for multipart upload with progress
            var transferUtility = new TransferUtility(_s3Client);
            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = _bucketName,
                Key = s3Key,
                FilePath = tempFile,
                ContentType = "application/gzip",
                PartSize = 10 * 1024 * 1024 // 10MB parts
            };

            uploadRequest.UploadProgressEvent += (sender, args) =>
            {
                progress?.Report(new BackupProgress(
                    fileSize / 2 + args.TransferredBytes,
                    fileSize,
                    Path.GetFileName(localBackupPath),
                    $"Uploading: {args.PercentDone}%"));
            };

            await transferUtility.UploadAsync(uploadRequest, cancellationToken);

            progress?.Report(new BackupProgress(fileSize, fileSize, Path.GetFileName(localBackupPath), "Upload complete"));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
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
            _logger.LogInformation("Downloading backup from S3: s3://{Bucket}/{Key}", _bucketName, remoteBackupPath);

            // Get object metadata for size
            var metadataRequest = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = remoteBackupPath
            };

            var metadata = await _s3Client.GetObjectMetadataAsync(metadataRequest, cancellationToken);
            var fileSize = metadata.ContentLength;
            var fileName = Path.GetFileName(remoteBackupPath);

            progress?.Report(new BackupProgress(0, fileSize, fileName, "Downloading from S3"));

            // Download to local temp file first
            var tempFile = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid()}.tar.gz");

            try
            {
                var transferUtility = new TransferUtility(_s3Client);
                var downloadRequest = new TransferUtilityDownloadRequest
                {
                    BucketName = _bucketName,
                    Key = remoteBackupPath,
                    FilePath = tempFile
                };

                downloadRequest.WriteObjectProgressEvent += (sender, args) =>
                {
                    progress?.Report(new BackupProgress(
                        args.TransferredBytes,
                        fileSize,
                        fileName,
                        $"Downloading: {args.PercentDone}%"));
                };

                await transferUtility.DownloadAsync(downloadRequest, cancellationToken);

                progress?.Report(new BackupProgress(fileSize, fileSize * 2, fileName, "Uploading to server"));

                // Upload to server
                var uploadResult = await _sshService.UploadFileAsync(_server, tempFile, localDestinationPath, cancellationToken);
                if (!uploadResult)
                {
                    throw new Exception("Failed to upload backup to server");
                }

                progress?.Report(new BackupProgress(fileSize * 2, fileSize * 2, fileName, "Download complete"));

                _logger.LogInformation("Backup downloaded successfully to {Path}", localDestinationPath);
                return localDestinationPath;
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download backup from S3");
            throw;
        }
    }

    public async Task<List<RemoteBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing backups in S3: s3://{Bucket}/{Prefix}", _bucketName, _keyPrefix);

            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = _keyPrefix
            };

            var backups = new List<RemoteBackupInfo>();
            ListObjectsV2Response response;

            do
            {
                response = await _s3Client.ListObjectsV2Async(request, cancellationToken);

                foreach (var obj in response.S3Objects)
                {
                    if (!obj.Key.EndsWith(".tar.gz")) continue;

                    var fileName = Path.GetFileName(obj.Key);
                    var backupId = fileName.Replace(".tar.gz", "");

                    // Try to get ETag as checksum (it's MD5 for single-part uploads)
                    var checksum = obj.ETag?.Trim('"');

                    backups.Add(new RemoteBackupInfo(
                        obj.Key,
                        backupId,
                        obj.LastModified,
                        obj.Size,
                        checksum));
                }

                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated);

            _logger.LogInformation("Found {Count} backups in S3", backups.Count);
            return backups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list backups from S3");
            throw;
        }
    }

    public async Task<bool> DeleteBackupAsync(string remoteBackupPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting backup from S3: s3://{Bucket}/{Key}", _bucketName, remoteBackupPath);

            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = remoteBackupPath
            };

            await _s3Client.DeleteObjectAsync(deleteRequest, cancellationToken);

            _logger.LogInformation("Backup deleted successfully from S3");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete backup from S3");
            return false;
        }
    }

    public async Task<bool> VerifyBackupExistsAsync(string remoteBackupPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var metadataRequest = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = remoteBackupPath
            };

            await _s3Client.GetObjectMetadataAsync(metadataRequest, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify backup existence in S3");
            return false;
        }
    }

    public async Task<BackupManifest?> GetBackupManifestAsync(
        string remoteBackupPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Reading manifest from S3 backup: s3://{Bucket}/{Key}", _bucketName, remoteBackupPath);

            // Download backup to temp file
            var tempFile = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid()}.tar.gz");

            try
            {
                var getRequest = new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = remoteBackupPath
                };

                using var response = await _s3Client.GetObjectAsync(getRequest, cancellationToken);
                await response.WriteResponseStreamToFileAsync(tempFile, false, cancellationToken);

                // Extract manifest.json from tar.gz
                var extractResult = await _sshService.ExecuteCommandAsync(
                    _server,
                    $"tar -xzf '{tempFile}' -O manifest.json 2>/dev/null || echo ''",
                    cancellationToken);

                if (extractResult.ExitCode != 0 || string.IsNullOrWhiteSpace(extractResult.Output))
                {
                    _logger.LogWarning("Failed to extract manifest from S3 backup");
                    return null;
                }

                var manifest = JsonSerializer.Deserialize<BackupManifest>(extractResult.Output);
                return manifest;
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read backup manifest from S3");
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Testing S3 connection to bucket: {Bucket}", _bucketName);

            // Try to list objects (with max 1 result) to test connection
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                MaxKeys = 1
            };

            await _s3Client.ListObjectsV2Async(request, cancellationToken);

            _logger.LogInformation("S3 connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 connection test failed");
            return false;
        }
    }
}
