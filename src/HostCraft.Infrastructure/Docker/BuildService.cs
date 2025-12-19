using System.Collections.Concurrent;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Implements Docker image building from source code.
/// </summary>
public class BuildService : IBuildService
{
    private readonly IDockerService _dockerService;
    private readonly HostCraftDbContext _context;
    private readonly ILogger<BuildService> _logger;
    private readonly ConcurrentDictionary<int, List<string>> _buildLogs = new();

    public BuildService(
        IDockerService dockerService,
        HostCraftDbContext context,
        ILogger<BuildService> logger)
    {
        _dockerService = dockerService;
        _context = context;
        _logger = logger;
    }

    public async Task<string> BuildImageAsync(
        Application application,
        string sourcePath,
        string? commitSha = null)
    {
        var deployment = await _context.Deployments
            .FirstOrDefaultAsync(d => 
                d.ApplicationId == application.Id && 
                d.CommitSha == commitSha &&
                d.Status == Core.Enums.DeploymentStatus.Running);

        var deploymentId = deployment?.Id ?? 0;

        try
        {
            // Generate image name
            var imageTag = !string.IsNullOrEmpty(commitSha) 
                ? commitSha.Substring(0, Math.Min(7, commitSha.Length))
                : "latest";
            
            var imageName = $"{application.Name.ToLower().Replace(" ", "-")}:{imageTag}";

            _logger.LogInformation(
                "Building Docker image {ImageName} from {SourcePath}",
                imageName,
                sourcePath);

            // Prepare build context
            var buildContext = Path.Combine(sourcePath, application.BuildContext ?? ".");
            var dockerfilePath = Path.Combine(buildContext, application.Dockerfile ?? "Dockerfile");

            if (!File.Exists(dockerfilePath))
            {
                throw new FileNotFoundException($"Dockerfile not found at {dockerfilePath}");
            }

            // Parse build args
            var buildArgs = ParseBuildArgs(application.BuildArgs);

            // Create tar archive of build context
            var tarStream = await CreateTarArchiveAsync(buildContext);

            // Build image using Docker service
            // Note: We'll need to pass the Server entity to the Build method
            var server = application.Server;
            
            var buildParameters = new ImageBuildParameters
            {
                Tags = new List<string> { imageName },
                Dockerfile = application.Dockerfile ?? "Dockerfile",
                BuildArgs = buildArgs,
                NoCache = false,
                Remove = true, // Remove intermediate containers
                ForceRemove = true,
            };

            AddLog(deploymentId, $"Building image {imageName}...");
            AddLog(deploymentId, $"Build context: {buildContext}");
            AddLog(deploymentId, $"Dockerfile: {application.Dockerfile}");

            var buildProgress = new Progress<string>(log =>
            {
                AddLog(deploymentId, log);
                _logger.LogDebug("Build: {Log}", log);
            });

            // Use the Docker service to build the image
            var buildRequest = new BuildImageRequest(
                Dockerfile: application.Dockerfile ?? "Dockerfile",
                Context: buildContext,
                Tag: imageName,
                BuildArgs: buildArgs
            );

            await _dockerService.BuildImageAsync(server, buildRequest, buildProgress);

            AddLog(deploymentId, $"Successfully built image {imageName}");
            _logger.LogInformation("Successfully built image {ImageName}", imageName);

            return imageName;
        }
        catch (Exception ex)
        {
            AddLog(deploymentId, $"Build failed: {ex.Message}");
            _logger.LogError(ex, "Failed to build image for application {App}", application.Name);
            throw;
        }
    }

    public async Task<bool> PushImageAsync(
        string imageName,
        string registryUrl,
        string? username = null,
        string? password = null)
    {
        try
        {
            _logger.LogInformation("Pushing image {ImageName} to {Registry}", imageName, registryUrl);

            // Tag the image for the target registry
            var targetImage = $"{registryUrl}/{imageName}";

            // Get application and server for Docker client access
            // For push operations, we need a server to connect to Docker
            var server = await _context.Servers.FirstOrDefaultAsync(s => s.Status == Core.Enums.ServerStatus.Online);
            if (server == null)
            {
                _logger.LogError("No online server available for image push");
                return false;
            }

            // Build auth config if credentials provided
            AuthConfig? authConfig = null;
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                authConfig = new AuthConfig
                {
                    Username = username,
                    Password = password,
                    ServerAddress = registryUrl
                };
            }

            // Tag the image for the registry
            await TagImageAsync(server, imageName, targetImage);

            // Push the image
            var pushProgress = new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrEmpty(msg.Status))
                {
                    _logger.LogDebug("Push progress: {Status}", msg.Status);
                }
            });

            // Use SSH to execute docker push command for remote registries
            var sshClient = GetSshClientForServer(server);
            if (sshClient != null)
            {
                var loginCmd = !string.IsNullOrEmpty(username)
                    ? $"echo '{password}' | docker login {registryUrl} -u {username} --password-stdin && "
                    : "";
                var pushCmd = sshClient.CreateCommand($"{loginCmd}docker push {targetImage}");
                var result = pushCmd.Execute();

                if (pushCmd.ExitStatus != 0)
                {
                    _logger.LogError("Push failed: {Error}", pushCmd.Error);
                    return false;
                }
            }

            _logger.LogInformation("Successfully pushed image {ImageName} to {Registry}", imageName, registryUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push image {ImageName}", imageName);
            return false;
        }
    }

    private async Task TagImageAsync(Server server, string sourceImage, string targetImage)
    {
        var sshClient = GetSshClientForServer(server);
        if (sshClient != null)
        {
            var tagCmd = sshClient.CreateCommand($"docker tag {sourceImage} {targetImage}");
            tagCmd.Execute();
            if (tagCmd.ExitStatus != 0)
            {
                throw new InvalidOperationException($"Failed to tag image: {tagCmd.Error}");
            }
        }
        await Task.CompletedTask;
    }

    private Renci.SshNet.SshClient? GetSshClientForServer(Server server)
    {
        if (server.Host == "localhost" || server.Host == "127.0.0.1")
            return null;

        if (server.PrivateKey == null || string.IsNullOrEmpty(server.PrivateKey.KeyData))
            throw new InvalidOperationException($"No private key configured for server {server.Name}");

        var keyFile = new Renci.SshNet.PrivateKeyFile(
            new MemoryStream(Encoding.UTF8.GetBytes(server.PrivateKey.KeyData)));
        var authMethod = new Renci.SshNet.PrivateKeyAuthenticationMethod(server.Username, keyFile);
        var connectionInfo = new Renci.SshNet.ConnectionInfo(server.Host, server.Port, server.Username, authMethod);
        var sshClient = new Renci.SshNet.SshClient(connectionInfo);
        sshClient.Connect();
        return sshClient;
    }

    private class AuthConfig
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? ServerAddress { get; set; }
    }

    private class JSONMessage
    {
        public string? Status { get; set; }
    }

    public async Task<List<string>> GetBuildLogsAsync(int deploymentId)
    {
        await Task.CompletedTask;
        
        if (_buildLogs.TryGetValue(deploymentId, out var logs))
        {
            return logs.ToList();
        }

        return new List<string>();
    }

    public async IAsyncEnumerable<string> StreamBuildLogsAsync(int deploymentId)
    {
        var lastIndex = 0;

        while (true)
        {
            if (_buildLogs.TryGetValue(deploymentId, out var logs))
            {
                for (int i = lastIndex; i < logs.Count; i++)
                {
                    yield return logs[i];
                    lastIndex = i + 1;
                }
            }

            await Task.Delay(100); // Poll every 100ms

            // Check if deployment is finished
            var deployment = await _context.Deployments.FindAsync(deploymentId);
            if (deployment != null && 
                (deployment.Status == Core.Enums.DeploymentStatus.Success || 
                 deployment.Status == Core.Enums.DeploymentStatus.Failed))
            {
                break;
            }
        }
    }

    private void AddLog(int deploymentId, string message)
    {
        if (deploymentId == 0) return;

        _buildLogs.AddOrUpdate(
            deploymentId,
            new List<string> { $"[{DateTime.UtcNow:HH:mm:ss}] {message}" },
            (key, existing) =>
            {
                existing.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
                return existing;
            });
    }

    private Dictionary<string, string> ParseBuildArgs(string? buildArgs)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrEmpty(buildArgs))
            return result;

        // Parse format: KEY1=VALUE1,KEY2=VALUE2
        var pairs = buildArgs.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                result[parts[0].Trim()] = parts[1].Trim();
            }
        }

        return result;
    }

    private async Task<Stream> CreateTarArchiveAsync(string sourceDirectory)
    {
        var tarStream = new MemoryStream();

        // Load .dockerignore patterns if present
        var ignorePatterns = await LoadDockerIgnorePatterns(sourceDirectory);

        // Get all files respecting .dockerignore
        var files = GetFilesForContext(sourceDirectory, ignorePatterns);

        // Create tar archive with proper headers
        await WriteTarArchive(tarStream, sourceDirectory, files);

        tarStream.Position = 0;
        return tarStream;
    }

    private async Task<List<string>> LoadDockerIgnorePatterns(string sourceDirectory)
    {
        var patterns = new List<string>();
        var dockerIgnorePath = Path.Combine(sourceDirectory, ".dockerignore");

        if (File.Exists(dockerIgnorePath))
        {
            var lines = await File.ReadAllLinesAsync(dockerIgnorePath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                {
                    patterns.Add(trimmed);
                }
            }
        }

        // Always ignore .git directory
        if (!patterns.Contains(".git"))
        {
            patterns.Add(".git");
        }

        return patterns;
    }

    private List<string> GetFilesForContext(string sourceDirectory, List<string> ignorePatterns)
    {
        var files = new List<string>();
        var allFiles = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);

        foreach (var file in allFiles)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');

            if (!ShouldIgnoreFile(relativePath, ignorePatterns))
            {
                files.Add(file);
            }
        }

        return files;
    }

    private bool ShouldIgnoreFile(string relativePath, List<string> ignorePatterns)
    {
        foreach (var pattern in ignorePatterns)
        {
            // Simple pattern matching (supports * and directory patterns)
            if (pattern.EndsWith("/") || pattern.EndsWith("\\"))
            {
                // Directory pattern
                var dirPattern = pattern.TrimEnd('/', '\\');
                if (relativePath.StartsWith(dirPattern + "/") || relativePath == dirPattern)
                    return true;
            }
            else if (pattern.Contains("*"))
            {
                // Wildcard pattern - simple glob matching
                var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*\\*", ".*")
                    .Replace("\\*", "[^/]*") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(relativePath, regex))
                    return true;
            }
            else
            {
                // Exact match or prefix match
                if (relativePath == pattern || relativePath.StartsWith(pattern + "/"))
                    return true;
            }
        }

        return false;
    }

    private async Task WriteTarArchive(MemoryStream tarStream, string sourceDirectory, List<string> files)
    {
        // TAR format: each file has a 512-byte header followed by file content padded to 512 bytes
        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
            var fileInfo = new FileInfo(filePath);
            var fileContent = await File.ReadAllBytesAsync(filePath);

            // Write TAR header (512 bytes)
            var header = CreateTarHeader(relativePath, fileContent.Length, fileInfo);
            await tarStream.WriteAsync(header, 0, 512);

            // Write file content
            await tarStream.WriteAsync(fileContent, 0, fileContent.Length);

            // Pad to 512-byte boundary
            var padding = 512 - (fileContent.Length % 512);
            if (padding < 512)
            {
                await tarStream.WriteAsync(new byte[padding], 0, padding);
            }
        }

        // Write end-of-archive marker (two 512-byte blocks of zeros)
        await tarStream.WriteAsync(new byte[1024], 0, 1024);
    }

    private byte[] CreateTarHeader(string fileName, long fileSize, FileInfo fileInfo)
    {
        var header = new byte[512];

        // File name (0-99, 100 bytes)
        var nameBytes = Encoding.ASCII.GetBytes(fileName.Length > 100 ? fileName.Substring(0, 100) : fileName);
        Array.Copy(nameBytes, 0, header, 0, nameBytes.Length);

        // File mode (100-107, 8 bytes) - 0644 for regular files
        var modeBytes = Encoding.ASCII.GetBytes("0000644\0");
        Array.Copy(modeBytes, 0, header, 100, 8);

        // Owner UID (108-115, 8 bytes)
        var uidBytes = Encoding.ASCII.GetBytes("0000000\0");
        Array.Copy(uidBytes, 0, header, 108, 8);

        // Group GID (116-123, 8 bytes)
        var gidBytes = Encoding.ASCII.GetBytes("0000000\0");
        Array.Copy(gidBytes, 0, header, 116, 8);

        // File size in octal (124-135, 12 bytes)
        var sizeOctal = Convert.ToString(fileSize, 8).PadLeft(11, '0') + "\0";
        var sizeBytes = Encoding.ASCII.GetBytes(sizeOctal);
        Array.Copy(sizeBytes, 0, header, 124, 12);

        // Modification time in octal (136-147, 12 bytes)
        var mtime = (long)(fileInfo.LastWriteTimeUtc - new DateTime(1970, 1, 1)).TotalSeconds;
        var mtimeOctal = Convert.ToString(mtime, 8).PadLeft(11, '0') + "\0";
        var mtimeBytes = Encoding.ASCII.GetBytes(mtimeOctal);
        Array.Copy(mtimeBytes, 0, header, 136, 12);

        // Checksum placeholder (148-155, 8 bytes) - fill with spaces initially
        for (int i = 148; i < 156; i++) header[i] = 0x20;

        // Type flag (156, 1 byte) - '0' for regular file
        header[156] = (byte)'0';

        // Link name (157-256, 100 bytes) - empty for regular files

        // USTAR magic (257-262, 6 bytes)
        var magic = Encoding.ASCII.GetBytes("ustar\0");
        Array.Copy(magic, 0, header, 257, 6);

        // USTAR version (263-264, 2 bytes)
        header[263] = (byte)'0';
        header[264] = (byte)'0';

        // Calculate and set checksum
        int checksum = 0;
        for (int i = 0; i < 512; i++) checksum += header[i];
        var checksumOctal = Convert.ToString(checksum, 8).PadLeft(6, '0') + "\0 ";
        var checksumBytes = Encoding.ASCII.GetBytes(checksumOctal);
        Array.Copy(checksumBytes, 0, header, 148, 8);

        return header;
    }
}
