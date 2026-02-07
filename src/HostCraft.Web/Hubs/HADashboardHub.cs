using Microsoft.AspNetCore.SignalR;
using HostCraft.Core.Models;
using HostCraft.Web.Services;

namespace HostCraft.Web.Hubs;

/// <summary>
/// SignalR hub for real-time HA dashboard updates.
/// Broadcasts cluster status updates every 5 seconds.
/// Tracks connected clients so the background service only polls when needed.
/// </summary>
public class HADashboardHub : Hub
{
    private readonly ILogger<HADashboardHub> _logger;

    // Track connected client count so the background service skips polling when nobody is listening
    private static int _connectedClients;
    public static int ConnectedClients => _connectedClients;

    public HADashboardHub(ILogger<HADashboardHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        Interlocked.Increment(ref _connectedClients);
        _logger.LogInformation("Client connected to HA Dashboard: {ConnectionId} (total: {Count})", Context.ConnectionId, _connectedClients);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Interlocked.Decrement(ref _connectedClients);
        _logger.LogInformation("Client disconnected from HA Dashboard: {ConnectionId} (total: {Count})", Context.ConnectionId, _connectedClients);
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Background service that periodically broadcasts cluster status updates.
/// Only polls the API when clients are connected AND an auth token is available.
/// </summary>
public class HADashboardBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IHubContext<HADashboardHub> _hubContext;
    private readonly ITokenStore _tokenStore;
    private readonly ILogger<HADashboardBackgroundService> _logger;
    private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(5);

    public HADashboardBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IHubContext<HADashboardHub> hubContext,
        ITokenStore tokenStore,
        ILogger<HADashboardBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _hubContext = hubContext;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HA Dashboard Background Service started");

        // Use PeriodicTimer for efficient periodic execution
        using var timer = new PeriodicTimer(_updateInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // Skip polling when no clients are connected - no point fetching data nobody will see
                if (HADashboardHub.ConnectedClients <= 0)
                {
                    continue;
                }

                // Skip polling when no auth token is available - the API will return 401
                var token = _tokenStore.GetAnyValidToken();
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogDebug("Skipping cluster status poll - no auth token available");
                    continue;
                }

                await BroadcastClusterStatusAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("HA Dashboard Background Service is stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in HA Dashboard Background Service");
            throw;
        }
    }

    private async Task BroadcastClusterStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Create a scope to resolve scoped services
            using var scope = _serviceScopeFactory.CreateScope();
            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

            // Call the API endpoint to get cluster status
            var httpClient = httpClientFactory.CreateClient("HostCraftAPI");
            var response = await httpClient.GetAsync("api/ha/cluster-status", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var clusterStatus = await response.Content.ReadFromJsonAsync<HAClusterStatusDto>(cancellationToken: cancellationToken);

                if (clusterStatus != null)
                {
                    // Broadcast to all connected clients
                    await _hubContext.Clients.All.SendAsync("ClusterStatusUpdate", clusterStatus, cancellationToken);

                    _logger.LogDebug("Broadcasted cluster status: {ManagerCount} managers, {WorkerCount} workers",
                        clusterStatus.OnlineManagers, clusterStatus.OnlineWorkers);
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogDebug("Cluster status request returned 401 - token may have expired");
            }
            else
            {
                _logger.LogWarning("Failed to fetch cluster status: {StatusCode}", response.StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation, don't log as error
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting cluster status");
            // Don't throw - we want the service to continue running
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HA Dashboard Background Service is stopping");
        await base.StopAsync(cancellationToken);
    }
}
