using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Health;

public class ServerHealthMetrics
{
    public int ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public ServerStatus Status { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public double DiskUsagePercent { get; set; }
    public long TotalMemoryMB { get; set; }
    public long UsedMemoryMB { get; set; }
    public int ContainerCount { get; set; }
    public int RunningContainers { get; set; }
    public DateTime LastChecked { get; set; }
    public string? ErrorMessage { get; set; }
}
