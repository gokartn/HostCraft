using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Diagnostics;
using Renci.SshNet;
using System.Text;

namespace HostCraft.Web.Hubs;

public class TerminalHub : Hub
{
    private static readonly ConcurrentDictionary<string, ITerminalSession> _sessions = new();
    private readonly ILogger<TerminalHub> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public TerminalHub(ILogger<TerminalHub> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task ConnectToServer(int serverId)
    {
        try
        {
            _logger.LogInformation("Connecting to server {ServerId}", serverId);
            
            // Get server details from API
            var httpClient = _httpClientFactory.CreateClient("HostCraftAPI");
            _logger.LogInformation("Using API URL: {BaseAddress}", httpClient.BaseAddress);
            
            var response = await httpClient.GetAsync($"api/servers/{serverId}");
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to fetch server details: {response.ReasonPhrase}");
            }
            
            var server = await response.Content.ReadFromJsonAsync<ServerDto>();
            if (server == null)
            {
                throw new Exception("Server not found");
            }

            // Check if this is localhost - use local shell instead of SSH
            if (IsLocalhost(server.Host))
            {
                await ConnectLocalTerminal(server);
            }
            else
            {
                // Remote server - requires SSH with private key
                if (server.PrivateKey == null || string.IsNullOrEmpty(server.PrivateKey.KeyData))
                {
                    throw new Exception("Private key is required for remote server connections. Please configure an SSH key for this server.");
                }
                await ConnectSshTerminal(server);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to server {ServerId}", serverId);
            await Clients.Caller.SendAsync("ConnectionFailed", ex.Message);
        }
    }

    private bool IsLocalhost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("127.0.0.1", StringComparison.Ordinal) ||
               host.Equals("::1", StringComparison.Ordinal);
    }

    private async Task ConnectLocalTerminal(ServerDto server)
    {
        _logger.LogInformation("Connecting to localhost terminal");

        // Check if we're running inside a Docker container
        bool isInContainer = false;
        try
        {
            isInContainer = File.Exists("/.dockerenv") || 
                           (File.Exists("/proc/self/cgroup") && 
                            File.ReadAllText("/proc/self/cgroup").Contains("docker"));
        }
        catch
        {
            isInContainer = false;
        }

        ProcessStartInfo processStartInfo;
        
        if (isInContainer)
        {
            // CRITICAL: When in container with "localhost" server, user wants HOST's terminal
            // Use nsenter to access host's PID namespace and spawn a shell there
            _logger.LogInformation("Detected container environment, accessing HOST terminal via nsenter");
            
            // nsenter enters the host's namespace through /proc/1/ns/
            // This requires privileged container or specific capabilities
            processStartInfo = new ProcessStartInfo
            {
                FileName = "nsenter",
                Arguments = "--target 1 --mount --uts --ipc --net --pid -- /bin/bash",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }
        else
        {
            // Not in container - use local shell directly
            _logger.LogInformation("Using local shell");
            var isWindows = OperatingSystem.IsWindows();
            processStartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "powershell.exe" : "/bin/bash",
                Arguments = isWindows ? "-NoLogo -NoProfile" : "",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
        }

        // Set environment for proper terminal behavior
        processStartInfo.Environment["TERM"] = "xterm-256color";

        var process = new Process { StartInfo = processStartInfo };
        
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start terminal process");
            throw new Exception($"Could not start terminal: {ex.Message}. If running in Docker, ensure container has CAP_SYS_ADMIN or --privileged for nsenter.");
        }

        var session = new LocalTerminalSession
        {
            Process = process,
            ServerId = server.Id
        };

        _sessions[Context.ConnectionId] = session;

        // Start reading output
        _ = Task.Run(async () => await ReadLocalOutput(Context.ConnectionId, process.StandardOutput));
        _ = Task.Run(async () => await ReadLocalError(Context.ConnectionId, process.StandardError));

        // Notify client of successful connection
        await Clients.Caller.SendAsync("ConnectionEstablished");
        var locationMsg = isInContainer ? "HOST machine shell" : "local shell";
        await Clients.Caller.SendAsync("ReceiveOutput", $"\x1b[32mConnected to localhost ({locationMsg})\x1b[0m\r\n");
        
        // Send initial newline to trigger prompt
        await Task.Delay(100);
        await process.StandardInput.WriteLineAsync("");
    }

    private async Task ConnectSshTerminal(ServerDto server)
    {
        _logger.LogInformation("Connecting via SSH to {Host}:{Port}", server.Host, server.Port);

        var connectionInfo = new Renci.SshNet.ConnectionInfo(
            server.Host,
            server.Port,
            server.Username,
            new PrivateKeyAuthenticationMethod(server.Username, 
                new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(server.PrivateKey!.KeyData))))
        );

        var client = new SshClient(connectionInfo);
        await Task.Run(() => client.Connect());

        var shell = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024);
        
        var session = new SshTerminalSession
        {
            Client = client,
            Shell = shell,
            ServerId = server.Id
        };

        _sessions[Context.ConnectionId] = session;

        // Start reading output
        _ = Task.Run(async () => await ReadSshOutput(Context.ConnectionId));

        // Notify client of successful connection
        await Clients.Caller.SendAsync("ConnectionEstablished");
        await Clients.Caller.SendAsync("ReceiveOutput", $"\x1b[32mConnected to {server.Host}\x1b[0m\r\n");
        
        // Get initial prompt
        var initialOutput = await ReadInitialPrompt(shell);
        if (!string.IsNullOrEmpty(initialOutput))
        {
            await Clients.Caller.SendAsync("UpdatePrompt", ExtractPrompt(initialOutput));
        }
    }

    public async Task SendInput(string input)
    {
        if (!_sessions.TryGetValue(Context.ConnectionId, out var session))
        {
            await Clients.Caller.SendAsync("ReceiveError", "Not connected to any server\n");
            return;
        }

        try
        {
            if (session is LocalTerminalSession localSession)
            {
                if (localSession.Process == null || localSession.Process.HasExited)
                {
                    await Clients.Caller.SendAsync("ReceiveError", "Terminal session is not active\n");
                    return;
                }
                await localSession.Process.StandardInput.WriteAsync(input);
                await localSession.Process.StandardInput.FlushAsync();
            }
            else if (session is SshTerminalSession sshSession)
            {
                if (sshSession.Shell == null || !sshSession.Shell.CanWrite)
                {
                    await Clients.Caller.SendAsync("ReceiveError", "Terminal session is not active\n");
                    return;
                }
                var bytes = Encoding.UTF8.GetBytes(input);
                await sshSession.Shell.WriteAsync(bytes, 0, bytes.Length);
                await sshSession.Shell.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send input");
            await Clients.Caller.SendAsync("ReceiveError", $"Input failed: {ex.Message}\n");
        }
    }

    public async Task ExecuteCommand(string command)
    {
        if (!_sessions.TryGetValue(Context.ConnectionId, out var session))
        {
            await Clients.Caller.SendAsync("ReceiveError", "Not connected to any server\n");
            return;
        }

        try
        {
            if (session is LocalTerminalSession localSession)
            {
                if (localSession.Process == null || localSession.Process.HasExited)
                {
                    await Clients.Caller.SendAsync("ReceiveError", "Terminal session is not active\n");
                    return;
                }
                await localSession.Process.StandardInput.WriteLineAsync(command);
            }
            else if (session is SshTerminalSession sshSession)
            {
                if (sshSession.Shell == null || !sshSession.Shell.CanWrite)
                {
                    await Clients.Caller.SendAsync("ReceiveError", "Terminal session is not active\n");
                    return;
                }
                var writer = new StreamWriter(sshSession.Shell, Encoding.UTF8) { AutoFlush = true };
                await writer.WriteLineAsync(command);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command: {Command}", command);
            await Clients.Caller.SendAsync("ReceiveError", $"Command execution failed: {ex.Message}\n");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_sessions.TryRemove(Context.ConnectionId, out var session))
        {
            session.Dispose();
            _logger.LogInformation("Terminal session closed for server {ServerId}", session.ServerId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task ReadLocalOutput(string connectionId, StreamReader reader)
    {
        var buffer = new char[4096];
        
        try
        {
            while (_sessions.ContainsKey(connectionId))
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    var output = new string(buffer, 0, bytesRead);
                    await Clients.Client(connectionId).SendAsync("ReceiveOutput", output);
                }
                else if (bytesRead == 0)
                {
                    await Task.Delay(10);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading local output for connection {ConnectionId}", connectionId);
            await Clients.Client(connectionId).SendAsync("ReceiveError", "Connection lost\n");
        }
    }

    private async Task ReadLocalError(string connectionId, StreamReader reader)
    {
        var buffer = new char[4096];
        
        try
        {
            while (_sessions.ContainsKey(connectionId))
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    var output = new string(buffer, 0, bytesRead);
                    await Clients.Client(connectionId).SendAsync("ReceiveOutput", $"\x1b[31m{output}\x1b[0m");
                }
                else if (bytesRead == 0)
                {
                    await Task.Delay(10);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading local stderr for connection {ConnectionId}", connectionId);
        }
    }

    private async Task ReadSshOutput(string connectionId)
    {
        if (!_sessions.TryGetValue(connectionId, out var baseSession) || baseSession is not SshTerminalSession session || session.Shell == null)
            return;

        var buffer = new byte[4096];
        
        try
        {
            while (session.Shell.CanRead && _sessions.ContainsKey(connectionId))
            {
                var bytesRead = await session.Shell.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    var output = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    await Clients.Client(connectionId).SendAsync("ReceiveOutput", output);
                }
                
                await Task.Delay(10);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading SSH output for connection {ConnectionId}", connectionId);
            await Clients.Client(connectionId).SendAsync("ReceiveError", "Connection lost\n");
        }
    }

    private async Task<string> ReadInitialPrompt(ShellStream shell)
    {
        var output = new StringBuilder();
        var buffer = new byte[1024];
        var timeout = DateTime.UtcNow.AddSeconds(3);

        while (DateTime.UtcNow < timeout)
        {
            if (shell.DataAvailable)
            {
                var bytesRead = await shell.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    output.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                }
            }
            else
            {
                await Task.Delay(100);
            }

            if (output.Length > 0 && (output.ToString().Contains('$') || output.ToString().Contains('#')))
            {
                break;
            }
        }

        return output.ToString();
    }

    private string ExtractPrompt(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lastLine = lines.LastOrDefault() ?? "user@server:~$";
        return CleanAnsiCodes(lastLine.Trim());
    }

    private string CleanAnsiCodes(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, @"\x1B\[[^@-~]*[@-~]", "");
    }
}

// Interface for terminal sessions
public interface ITerminalSession : IDisposable
{
    int ServerId { get; }
}

// Local terminal session using Process
public class LocalTerminalSession : ITerminalSession
{
    public Process? Process { get; set; }
    public int ServerId { get; set; }

    public void Dispose()
    {
        if (Process != null && !Process.HasExited)
        {
            try
            {
                Process.Kill();
            }
            catch { /* Ignore */ }
        }
        Process?.Dispose();
    }
}

// SSH terminal session
public class SshTerminalSession : ITerminalSession
{
    public SshClient? Client { get; set; }
    public ShellStream? Shell { get; set; }
    public int ServerId { get; set; }

    public void Dispose()
    {
        Shell?.Dispose();
        Client?.Disconnect();
        Client?.Dispose();
    }
}

public class ServerDto
{
    public int Id { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Username { get; set; } = "";
    public PrivateKeyDto? PrivateKey { get; set; }
}

public class PrivateKeyDto
{
    public int Id { get; set; }
    public string KeyData { get; set; } = "";
}
