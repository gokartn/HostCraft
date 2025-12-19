using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Text;

namespace HostCraft.Web.Hubs;

/// <summary>
/// SignalR hub for real-time log streaming from Docker containers and services.
/// </summary>
public class LogStreamHub : Hub
{
    private static readonly ConcurrentDictionary<string, LogStreamSession> _sessions = new();
    private readonly ILogger<LogStreamHub> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LogStreamHub(ILogger<LogStreamHub> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Start streaming logs for a container on a server.
    /// </summary>
    public async Task StreamContainerLogs(int serverId, string containerId, bool follow = true, int tailLines = 100)
    {
        try
        {
            _logger.LogInformation("Starting container log stream for {ContainerId} on server {ServerId}",
                containerId, serverId);

            var httpClient = _httpClientFactory.CreateClient("HostCraftAPI");
            var server = await GetServerAsync(httpClient, serverId);

            if (server == null)
            {
                await Clients.Caller.SendAsync("LogError", "Server not found");
                return;
            }

            var session = new LogStreamSession
            {
                ServerId = serverId,
                ContainerId = containerId,
                ConnectionId = Context.ConnectionId,
                CancellationTokenSource = new CancellationTokenSource()
            };

            _sessions[Context.ConnectionId] = session;

            // Notify client that streaming has started
            await Clients.Caller.SendAsync("LogStreamStarted", containerId);

            // Start streaming in background
            _ = Task.Run(async () => await StreamContainerLogsAsync(session, server, follow, tailLines));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start container log stream");
            await Clients.Caller.SendAsync("LogError", ex.Message);
        }
    }

    /// <summary>
    /// Start streaming logs for a swarm service on a server.
    /// </summary>
    public async Task StreamServiceLogs(int serverId, string serviceId, bool follow = true, int tailLines = 100)
    {
        try
        {
            _logger.LogInformation("Starting service log stream for {ServiceId} on server {ServerId}",
                serviceId, serverId);

            var httpClient = _httpClientFactory.CreateClient("HostCraftAPI");
            var server = await GetServerAsync(httpClient, serverId);

            if (server == null)
            {
                await Clients.Caller.SendAsync("LogError", "Server not found");
                return;
            }

            var session = new LogStreamSession
            {
                ServerId = serverId,
                ServiceId = serviceId,
                ConnectionId = Context.ConnectionId,
                CancellationTokenSource = new CancellationTokenSource()
            };

            _sessions[Context.ConnectionId] = session;

            // Notify client that streaming has started
            await Clients.Caller.SendAsync("LogStreamStarted", serviceId);

            // Start streaming in background
            _ = Task.Run(async () => await StreamServiceLogsAsync(session, server, follow, tailLines));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start service log stream");
            await Clients.Caller.SendAsync("LogError", ex.Message);
        }
    }

    /// <summary>
    /// Start streaming deployment logs.
    /// </summary>
    public async Task StreamDeploymentLogs(int deploymentId)
    {
        try
        {
            _logger.LogInformation("Starting deployment log stream for deployment {DeploymentId}", deploymentId);

            var session = new LogStreamSession
            {
                DeploymentId = deploymentId,
                ConnectionId = Context.ConnectionId,
                CancellationTokenSource = new CancellationTokenSource()
            };

            _sessions[Context.ConnectionId] = session;

            // Notify client that streaming has started
            await Clients.Caller.SendAsync("LogStreamStarted", $"deployment-{deploymentId}");

            // Start streaming deployment logs from database
            _ = Task.Run(async () => await StreamDeploymentLogsAsync(session));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start deployment log stream");
            await Clients.Caller.SendAsync("LogError", ex.Message);
        }
    }

    /// <summary>
    /// Stop streaming logs.
    /// </summary>
    public async Task StopStream()
    {
        if (_sessions.TryRemove(Context.ConnectionId, out var session))
        {
            session.CancellationTokenSource.Cancel();
            session.Dispose();
            _logger.LogInformation("Stopped log stream for connection {ConnectionId}", Context.ConnectionId);
        }

        await Clients.Caller.SendAsync("LogStreamStopped");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_sessions.TryRemove(Context.ConnectionId, out var session))
        {
            session.CancellationTokenSource.Cancel();
            session.Dispose();
            _logger.LogInformation("Log stream session closed for connection {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<LogStreamServerDto?> GetServerAsync(HttpClient httpClient, int serverId)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/servers/{serverId}");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<LogStreamServerDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch server {ServerId}", serverId);
            return null;
        }
    }

    private async Task StreamContainerLogsAsync(LogStreamSession session, LogStreamServerDto server, bool follow, int tailLines)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("HostCraftAPI");

            // Use the API to get logs via streaming endpoint
            var followParam = follow ? "true" : "false";
            var url = $"api/containers/{session.ContainerId}/logs?serverId={server.Id}&follow={followParam}&tail={tailLines}";

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, session.CancellationTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                await Clients.Client(session.ConnectionId).SendAsync("LogError", "Failed to start log stream");
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync(session.CancellationTokenSource.Token);
            using var reader = new StreamReader(stream);

            var buffer = new char[4096];

            while (!session.CancellationTokenSource.Token.IsCancellationRequested)
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    var logData = new string(buffer, 0, bytesRead);
                    await Clients.Client(session.ConnectionId).SendAsync("LogData", logData);
                }
                else if (bytesRead == 0 && !follow)
                {
                    break;
                }
                else
                {
                    await Task.Delay(100, session.CancellationTokenSource.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming container logs");
            await Clients.Client(session.ConnectionId).SendAsync("LogError", ex.Message);
        }
        finally
        {
            await Clients.Client(session.ConnectionId).SendAsync("LogStreamEnded");
        }
    }

    private async Task StreamServiceLogsAsync(LogStreamSession session, LogStreamServerDto server, bool follow, int tailLines)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("HostCraftAPI");

            // Use the API to get service logs via streaming endpoint
            var followParam = follow ? "true" : "false";
            var url = $"api/services/{session.ServiceId}/logs?serverId={server.Id}&follow={followParam}&tail={tailLines}";

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, session.CancellationTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                await Clients.Client(session.ConnectionId).SendAsync("LogError", "Failed to start log stream");
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync(session.CancellationTokenSource.Token);
            using var reader = new StreamReader(stream);

            var buffer = new char[4096];

            while (!session.CancellationTokenSource.Token.IsCancellationRequested)
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    var logData = new string(buffer, 0, bytesRead);
                    await Clients.Client(session.ConnectionId).SendAsync("LogData", logData);
                }
                else if (bytesRead == 0 && !follow)
                {
                    break;
                }
                else
                {
                    await Task.Delay(100, session.CancellationTokenSource.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming service logs");
            await Clients.Client(session.ConnectionId).SendAsync("LogError", ex.Message);
        }
        finally
        {
            await Clients.Client(session.ConnectionId).SendAsync("LogStreamEnded");
        }
    }

    private async Task StreamDeploymentLogsAsync(LogStreamSession session)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("HostCraftAPI");
            var lastLogId = 0;

            while (!session.CancellationTokenSource.Token.IsCancellationRequested)
            {
                // Poll for new deployment logs
                var url = $"api/deployments/{session.DeploymentId}/logs?afterId={lastLogId}";
                var response = await httpClient.GetAsync(url, session.CancellationTokenSource.Token);

                if (response.IsSuccessStatusCode)
                {
                    var logs = await response.Content.ReadFromJsonAsync<DeploymentLogResponse[]>(cancellationToken: session.CancellationTokenSource.Token);

                    if (logs != null && logs.Length > 0)
                    {
                        foreach (var log in logs)
                        {
                            var levelColor = log.Level switch
                            {
                                "Error" => "\x1b[31m", // Red
                                "Warning" => "\x1b[33m", // Yellow
                                "Success" => "\x1b[32m", // Green
                                _ => "\x1b[0m" // Default
                            };

                            var formattedLog = $"{levelColor}[{log.Timestamp:HH:mm:ss}] {log.Message}\x1b[0m\r\n";
                            await Clients.Client(session.ConnectionId).SendAsync("LogData", formattedLog);

                            if (log.Id > lastLogId)
                            {
                                lastLogId = log.Id;
                            }
                        }
                    }

                    // Check if deployment is complete
                    var statusResponse = await httpClient.GetAsync($"api/deployments/{session.DeploymentId}/status", session.CancellationTokenSource.Token);
                    if (statusResponse.IsSuccessStatusCode)
                    {
                        var status = await statusResponse.Content.ReadFromJsonAsync<DeploymentStatusResponse>(cancellationToken: session.CancellationTokenSource.Token);
                        if (status?.Status is "Success" or "Failed" or "Cancelled")
                        {
                            await Clients.Client(session.ConnectionId).SendAsync("DeploymentComplete", status.Status);
                            break;
                        }
                    }
                }

                await Task.Delay(1000, session.CancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming deployment logs");
            await Clients.Client(session.ConnectionId).SendAsync("LogError", ex.Message);
        }
        finally
        {
            await Clients.Client(session.ConnectionId).SendAsync("LogStreamEnded");
        }
    }

    /// <summary>
    /// Broadcast a log message to all clients watching a specific deployment.
    /// Called from DeploymentService when new logs are added.
    /// </summary>
    public static async Task BroadcastDeploymentLog(IHubContext<LogStreamHub> hubContext, int deploymentId, string level, string message)
    {
        var levelColor = level switch
        {
            "Error" => "\x1b[31m",
            "Warning" => "\x1b[33m",
            "Success" => "\x1b[32m",
            _ => "\x1b[0m"
        };

        var formattedLog = $"{levelColor}[{DateTime.UtcNow:HH:mm:ss}] {message}\x1b[0m\r\n";

        // Find all sessions watching this deployment
        foreach (var session in _sessions.Values.Where(s => s.DeploymentId == deploymentId))
        {
            await hubContext.Clients.Client(session.ConnectionId).SendAsync("LogData", formattedLog);
        }
    }
}

public class LogStreamSession : IDisposable
{
    public int ServerId { get; set; }
    public string? ContainerId { get; set; }
    public string? ServiceId { get; set; }
    public int? DeploymentId { get; set; }
    public required string ConnectionId { get; set; }
    public required CancellationTokenSource CancellationTokenSource { get; set; }

    public void Dispose()
    {
        CancellationTokenSource?.Dispose();
    }
}

public class LogStreamServerDto
{
    public int Id { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Username { get; set; } = "";
}

public class DeploymentLogResponse
{
    public int Id { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class DeploymentStatusResponse
{
    public string Status { get; set; } = "";
}
