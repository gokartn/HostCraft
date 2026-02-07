using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using Microsoft.Extensions.Logging;
using File = Google.Apis.Drive.v3.Data.File;

namespace HostCraft.Infrastructure.Backups;

/// <summary>
/// Google Drive storage provider for backups
/// </summary>
public class GoogleDriveBackupStorageProvider : IBackupStorageProvider
{
    private readonly ILogger<GoogleDriveBackupStorageProvider> _logger;
    private readonly ISshService _sshService;
    private readonly Server _server;
    private readonly DriveService _driveService;
    private readonly string _folderId;

    public GoogleDriveBackupStorageProvider(
        ILogger<GoogleDriveBackupStorageProvider> logger,
        ISshService sshService,
        Server server,
        GoogleDriveBackupConfig config)
    {
        _logger = logger;
        _sshService = sshService;
        _server = server;
        _folderId = config.FolderId;

        // Initialize Google Drive service with OAuth2 credentials
        UserCredential credential;

        if (!string.IsNullOrEmpty(config.ServiceAccountJson))
        {
            // Service account authentication
            var serviceAccountCredential = GoogleCredential.FromJson(config.ServiceAccountJson)
                .CreateScoped(DriveService.ScopeConstants.Drive);

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = serviceAccountCredential,
                ApplicationName = "HostCraft Backup System"
            });
        }
        else
        {
            // OAuth2 user authentication with refresh token
            credential = new UserCredential(
                new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow(
                    new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow.Initializer
                    {
                        ClientSecrets = new ClientSecrets
                        {
                            ClientId = config.ClientId,
                            ClientSecret = config.ClientSecret
                        }
                    }),
                "user",
                new Google.Apis.Auth.OAuth2.Responses.TokenResponse
                {
                    RefreshToken = config.RefreshToken
                });

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "HostCraft Backup System"
            });
        }
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
            _logger.LogInformation("Uploading backup to Google Drive: {FileName}", fileName);

            // Get file size for progress reporting
            var sizeResult = await _sshService.ExecuteCommandAsync(
                _server,
                $"stat -c%s '{localBackupPath}' 2>/dev/null || echo 0",
                cancellationToken);

            var fileSize = long.TryParse(sizeResult.Output.Trim(), out var size) ? size : 0;

            progress?.Report(new BackupProgress(0, fileSize, fileName, "Downloading from server"));

            // Download file from server to local temp
            var tempFile = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid()}.tar.gz");

            try
            {
                var scpResult = await _sshService.DownloadFileAsync(_server, localBackupPath, tempFile, cancellationToken);
                if (!scpResult)
                {
                    throw new Exception("Failed to download backup from server");
                }

                progress?.Report(new BackupProgress(fileSize / 2, fileSize, fileName, "Uploading to Google Drive"));

                // Create file metadata
                var fileMetadata = new File
                {
                    Name = fileName,
                    Parents = new List<string> { _folderId },
                    Description = $"HostCraft backup created at {manifest.CreatedAt:yyyy-MM-dd HH:mm:ss}"
                };

                // Upload file to Google Drive
                using var stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read);
                var request = _driveService.Files.Create(fileMetadata, stream, "application/gzip");

                // Track upload progress
                request.ProgressChanged += uploadProgress =>
                {
                    if (uploadProgress.Status == UploadStatus.Uploading)
                    {
                        var bytesUploaded = fileSize / 2 + uploadProgress.BytesSent;
                        progress?.Report(new BackupProgress(
                            bytesUploaded,
                            fileSize,
                            fileName,
                            $"Uploading: {(uploadProgress.BytesSent * 100 / fileSize)}%"));
                    }
                };

                request.Fields = "id, name, size, createdTime, md5Checksum";
                var uploadedFile = await request.UploadAsync(cancellationToken);

                if (uploadedFile.Status != UploadStatus.Completed)
                {
                    throw new Exception($"Upload failed with status: {uploadedFile.Status}");
                }

                progress?.Report(new BackupProgress(fileSize, fileSize, fileName, "Upload complete"));

                var file = request.ResponseBody;
                _logger.LogInformation("Backup uploaded successfully to Google Drive: {FileId}", file.Id);

                return file.Id; // Return Google Drive file ID
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload backup to Google Drive");
            throw;
        }
    }

    public async Task<string> DownloadBackupAsync(
        string remoteBackupPath, // This is the Google Drive file ID
        string localDestinationPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading backup from Google Drive: {FileId}", remoteBackupPath);

            // Get file metadata
            var getRequest = _driveService.Files.Get(remoteBackupPath);
            getRequest.Fields = "id, name, size, md5Checksum";
            var file = await getRequest.ExecuteAsync(cancellationToken);

            var fileSize = file.Size ?? 0;
            var fileName = file.Name;

            progress?.Report(new BackupProgress(0, fileSize, fileName, "Downloading from Google Drive"));

            // Download to local temp file
            var tempFile = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid()}.tar.gz");

            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                {
                    var downloadRequest = _driveService.Files.Get(remoteBackupPath);
                    
                    downloadRequest.MediaDownloader.ProgressChanged += downloadProgress =>
                    {
                        if (downloadProgress.Status == Google.Apis.Download.DownloadStatus.Downloading)
                        {
                            progress?.Report(new BackupProgress(
                                downloadProgress.BytesDownloaded,
                                fileSize,
                                fileName,
                                $"Downloading: {(downloadProgress.BytesDownloaded * 100 / fileSize)}%"));
                        }
                    };

                    await downloadRequest.DownloadAsync(stream, cancellationToken);
                }

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
                if (System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download backup from Google Drive");
            throw;
        }
    }

    public async Task<List<RemoteBackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing backups in Google Drive folder: {FolderId}", _folderId);

            var backups = new List<RemoteBackupInfo>();

            // List all .tar.gz files in the folder
            var listRequest = _driveService.Files.List();
            listRequest.Q = $"'{_folderId}' in parents and name contains '.tar.gz' and trashed = false";
            listRequest.Fields = "nextPageToken, files(id, name, size, createdTime, md5Checksum)";
            listRequest.PageSize = 100;

            string? pageToken = null;
            do
            {
                listRequest.PageToken = pageToken;
                var result = await listRequest.ExecuteAsync(cancellationToken);

                foreach (var file in result.Files)
                {
                    var backupId = file.Name.Replace(".tar.gz", "");
                    var createdAt = file.CreatedTimeDateTimeOffset?.DateTime ?? DateTime.UtcNow;

                    backups.Add(new RemoteBackupInfo(
                        file.Id, // Use file ID as path
                        backupId,
                        createdAt,
                        file.Size ?? 0,
                        file.Md5Checksum));
                }

                pageToken = result.NextPageToken;
            } while (pageToken != null);

            _logger.LogInformation("Found {Count} backups in Google Drive", backups.Count);
            return backups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list backups from Google Drive");
            throw;
        }
    }

    public async Task<bool> DeleteBackupAsync(string remoteBackupPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting backup from Google Drive: {FileId}", remoteBackupPath);

            await _driveService.Files.Delete(remoteBackupPath).ExecuteAsync(cancellationToken);

            _logger.LogInformation("Backup deleted successfully from Google Drive");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete backup from Google Drive");
            return false;
        }
    }

    public async Task<bool> VerifyBackupExistsAsync(string remoteBackupPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var getRequest = _driveService.Files.Get(remoteBackupPath);
            getRequest.Fields = "id";
            await getRequest.ExecuteAsync(cancellationToken);
            return true;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify backup existence in Google Drive");
            return false;
        }
    }

    public async Task<BackupManifest?> GetBackupManifestAsync(
        string remoteBackupPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Reading manifest from Google Drive backup: {FileId}", remoteBackupPath);

            // Download backup to temp file
            var tempFile = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid()}.tar.gz");

            try
            {
                using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                {
                    var downloadRequest = _driveService.Files.Get(remoteBackupPath);
                    await downloadRequest.DownloadAsync(stream, cancellationToken);
                }

                // Extract manifest.json from tar.gz
                var extractResult = await _sshService.ExecuteCommandAsync(
                    _server,
                    $"tar -xzf '{tempFile}' -O manifest.json 2>/dev/null || echo ''",
                    cancellationToken);

                if (extractResult.ExitCode != 0 || string.IsNullOrWhiteSpace(extractResult.Output))
                {
                    _logger.LogWarning("Failed to extract manifest from Google Drive backup");
                    return null;
                }

                var manifest = JsonSerializer.Deserialize<BackupManifest>(extractResult.Output);
                return manifest;
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read backup manifest from Google Drive");
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Testing Google Drive connection");

            // Try to get folder metadata to test connection
            var getRequest = _driveService.Files.Get(_folderId);
            getRequest.Fields = "id, name";
            var folder = await getRequest.ExecuteAsync(cancellationToken);

            _logger.LogInformation("Google Drive connection test successful (folder: {FolderName})", folder.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Drive connection test failed");
            return false;
        }
    }
}
