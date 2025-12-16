using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using Renci.SshNet;
using System.Text;

namespace HostCraft.Web.Hubs;

public class TerminalHub : Hub
{
    private static readonly ConcurrentDictionary<string, SshTerminalSession> _sessions = new();
    private readonly ILogger<TerminalHub> _logger;
    private readonly HttpClient _httpClient;

    public TerminalHub(ILogger<TerminalHub> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task ConnectToServer(int serverId)
    {
        try
        {
            // Get server details from API
            var response = await _httpClient.GetAsync($"http://localhost:5100/api/servers/{serverId}");
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to fetch server details: {response.ReasonPhrase}");
            }
            
            var server = await response.Content.ReadFromJsonAsync<ServerDto>();
            if (server == null || server.PrivateKey == null)
            {
                throw new Exception("Server or private key not found");
            }
            
            var host = server.Host;
            var port = server.Port;
            var username = server.Username;
            var privateKeyData = server.PrivateKey.KeyData;

            var connectionInfo = new Renci.SshNet.ConnectionInfo(
                host,
                port,
                username,
                new PrivateKeyAuthenticationMethod(username, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(privateKeyData))))
            );

            var client = new SshClient(connectionInfo);
            await Task.Run(() => client.Connect());

            var shell = client.CreateShellStream("xterm", 80, 24, 800, 600, 1024);
            
            var session = new SshTerminalSession
            {
                Client = client,
                Shell = shell,
                ServerId = serverId
            };

            _sessions[Context.ConnectionId] = session;

            // Start reading output
            _ = Task.Run(async () => await ReadShellOutput(Context.ConnectionId));

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to server {ServerId}", serverId);
            await Clients.Caller.SendAsync("ConnectionFailed", ex.Message);
            await Clients.Caller.SendAsync("ReceiveError", $"\x1b[31mConnection failed: {ex.Message}\x1b[0m\r\n");
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
            if (session.Shell == null || !session.Shell.CanWrite)
            {
                await Clients.Caller.SendAsync("ReceiveError", "Terminal session is not active\n");
                return;
            }

            // Write command to shell
            var writer = new StreamWriter(session.Shell, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync(command);
            
            // Output is read by the background task
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command: {Command}", command);
            await Clients.Caller.SendAsync("ReceiveError", $"Command execution failed: {ex.Message}\n");
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
            if (session.Shell == null || !session.Shell.CanWrite)
            {
                await Clients.Caller.SendAsync("ReceiveError", "Terminal session is not active\n");
                return;
            }

            // Write raw input to shell (for XTerm.js integration)
            var bytes = Encoding.UTF8.GetBytes(input);
            await session.Shell.WriteAsync(bytes, 0, bytes.Length);
            await session.Shell.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send input");
            await Clients.Caller.SendAsync("ReceiveError", $"Input failed: {ex.Message}\n");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_sessions.TryRemove(Context.ConnectionId, out var session))
        {
            session.Shell?.Dispose();
            session.Client?.Disconnect();
            session.Client?.Dispose();
            _logger.LogInformation("Terminal session closed for server {ServerId}", session.ServerId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task ReadShellOutput(string connectionId)
    {
        if (!_sessions.TryGetValue(connectionId, out var session) || session.Shell == null)
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
                    
                    // Send raw output with ANSI codes (XTerm.js will handle them)
                    await Clients.Client(connectionId).SendAsync("ReceiveOutput", output);
                }
                
                await Task.Delay(10); // Small delay to prevent CPU spinning
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading shell output for connection {ConnectionId}", connectionId);
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
        // Extract prompt from output (usually ends with $ or #)
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lastLine = lines.LastOrDefault() ?? "user@server:~$";
        
        // Clean and return prompt
        return CleanAnsiCodes(lastLine.Trim());
    }

    private string CleanAnsiCodes(string input)
    {
        // Remove ANSI escape sequences for simplified terminal display
        // In a production app, you'd want to properly handle these for colors/formatting
        return System.Text.RegularExpressions.Regex.Replace(input, @"\x1B\[[^@-~]*[@-~]", "");
    }
}

public class SshTerminalSession
{
    public SshClient? Client { get; set; }
    public ShellStream? Shell { get; set; }
    public int ServerId { get; set; }
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
