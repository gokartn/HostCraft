namespace HostCraft.Api.Models.Health;

public class DashboardHealthResponse
{
    public int TotalServers { get; set; }
    public int OnlineServers { get; set; }
    public int TotalContainers { get; set; }
    public int RunningContainers { get; set; }
    public double AverageCpuUsage { get; set; }
    public double AverageMemoryUsage { get; set; }
    public List<ServerHealthMetrics> ServerMetrics { get; set; } = new();
}
