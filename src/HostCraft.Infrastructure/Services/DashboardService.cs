using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Models;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly IServerRepository _serverRepository;
    private readonly IHealthCheckRepository _healthCheckRepository;
    private readonly IDockerService _dockerService;
    private readonly INodeMetricsService _nodeMetricsService;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        IServerRepository serverRepository,
        IHealthCheckRepository healthCheckRepository,
        IDockerService dockerService,
        INodeMetricsService nodeMetricsService,
        ILogger<DashboardService> logger)
    {
        _serverRepository = serverRepository;
        _healthCheckRepository = healthCheckRepository;
        _dockerService = dockerService;
        _nodeMetricsService = nodeMetricsService;
        _logger = logger;
    }

    public async Task<HAClusterStatusDto> GetClusterStatusAsync(CancellationToken cancellationToken = default)
    {
        var managers = (await _serverRepository.GetSwarmManagersWithRegionAsync(cancellationToken)).ToList();
        if (!managers.Any())
        {
            return CreateEmptyClusterStatus();
        }

        var activeManager = managers.FirstOrDefault(m => m.Status == ServerStatus.Online) ?? managers.First();
        if (activeManager == null)
        {
            return CreateEmptyClusterStatus();
        }

        try
        {
            var nodes = (await _dockerService.ListNodesAsync(activeManager, cancellationToken)).ToList();
            var services = (await _dockerService.ListServicesAsync(activeManager, cancellationToken)).ToList();
            var swarmInfo = await _dockerService.InspectSwarmAsync(activeManager, cancellationToken);

            var managerNodes = nodes.Where(n => n.Role.Equals("manager", StringComparison.OrdinalIgnoreCase)).ToList();
            var workerNodes = nodes.Where(n => n.Role.Equals("worker", StringComparison.OrdinalIgnoreCase)).ToList();

            int totalManagers = managerNodes.Count;
            int onlineManagers = managerNodes.Count(n => n.State.Equals("ready", StringComparison.OrdinalIgnoreCase));
            int totalWorkers = workerNodes.Count;
            int onlineWorkers = workerNodes.Count(n => n.State.Equals("ready", StringComparison.OrdinalIgnoreCase));

            var leaderNode = managerNodes.FirstOrDefault(n => n.IsLeader);
            var nodeDtos = await BuildNodeDtos(nodes, managers, cancellationToken);
            var metricsDict = await _nodeMetricsService.GetAllNodeMetricsAsync(
                nodeDtos.Where(n => n.ServerId.HasValue).Select(n => n.ServerId!.Value),
                cancellationToken);

            nodeDtos = nodeDtos.Select(node =>
            {
                if (node.ServerId.HasValue && metricsDict.TryGetValue(node.ServerId.Value, out var metrics))
                {
                    return node with { Metrics = metrics };
                }
                return node;
            }).ToList();

            var regionDtos = BuildRegionDtos(nodeDtos);
            var serviceDtos = await BuildServiceDtos(services, cancellationToken);
            var recommendations = GenerateRecommendations(onlineManagers, totalWorkers, serviceDtos);

            return new HAClusterStatusDto(
                ClusterId: swarmInfo?.Id,
                TotalManagers: totalManagers,
                OnlineManagers: onlineManagers,
                TotalWorkers: totalWorkers,
                OnlineWorkers: onlineWorkers,
                HasQuorum: onlineManagers >= 3,
                LeaderNodeId: leaderNode?.Id,
                LeaderHostname: leaderNode?.Hostname,
                QuorumStatus: CalculateQuorumStatus(onlineManagers),
                Nodes: nodeDtos,
                Regions: regionDtos,
                Services: serviceDtos,
                Recommendations: recommendations,
                Timestamp: DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect cluster status");
            return CreateEmptyClusterStatus();
        }
    }

    public async Task<HAHistoricalDataDto> GetHistoryAsync(int hours, CancellationToken cancellationToken = default)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-hours);
        int intervalMinutes = hours > 6 ? 30 : 5;

        var managerHealthChecks = await _healthCheckRepository.GetByServerTypeInRangeAsync(
            ServerType.SwarmManager,
            startTime,
            endTime,
            cancellationToken);

        var workerHealthChecks = await _healthCheckRepository.GetByServerTypeInRangeAsync(
            ServerType.SwarmWorker,
            startTime,
            endTime,
            cancellationToken);

        var managerAvailability = AggregateHealthData(managerHealthChecks, startTime, endTime, intervalMinutes);
        var workerAvailability = AggregateHealthData(workerHealthChecks, startTime, endTime, intervalMinutes);

        var quorumStatus = managerAvailability.Select(point => new HAMetricPoint(
            Timestamp: point.Timestamp,
            Value: point.Value >= 3 ? 1.0 : point.Value >= 2 ? 0.5 : 0.0,
            Label: point.Value >= 3 ? "healthy" : point.Value >= 2 ? "degraded" : "critical"
        )).ToList();

        var totalNodes = managerAvailability.Zip(workerAvailability, (m, w) => new HAMetricPoint(
            Timestamp: m.Timestamp,
            Value: m.Value + w.Value,
            Label: null
        )).ToList();

        var serviceHealth = new List<HAMetricPoint>
        {
            new(DateTime.UtcNow, 0, "No historical data")
        };

        return new HAHistoricalDataDto(
            ManagerAvailability: managerAvailability,
            WorkerAvailability: workerAvailability,
            QuorumStatus: quorumStatus,
            TotalNodes: totalNodes,
            ServiceHealth: serviceHealth,
            StartTime: startTime,
            EndTime: endTime,
            IntervalMinutes: intervalMinutes);
    }

    public Task<HANodeMetricsDto?> GetNodeMetricsAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return _nodeMetricsService.GetNodeMetricsAsync(serverId, cancellationToken);
    }

    private HAClusterStatusDto CreateEmptyClusterStatus()
    {
        return new HAClusterStatusDto(
            ClusterId: null,
            TotalManagers: 0,
            OnlineManagers: 0,
            TotalWorkers: 0,
            OnlineWorkers: 0,
            HasQuorum: false,
            LeaderNodeId: null,
            LeaderHostname: null,
            QuorumStatus: "critical",
            Nodes: new List<HANodeDto>(),
            Regions: new List<HARegionDto>(),
            Services: new List<HAServiceStatusDto>(),
            Recommendations: new List<string> { "No swarm managers found. Initialize a swarm cluster." },
            Timestamp: DateTime.UtcNow);
    }

    private static string CalculateQuorumStatus(int onlineManagers)
    {
        return onlineManagers >= 3 ? "healthy" : onlineManagers >= 2 ? "degraded" : "critical";
    }

    private Task<List<HANodeDto>> BuildNodeDtos(List<NodeInfo> nodes, List<Core.Entities.Server> managers, CancellationToken cancellationToken)
    {
        var nodeDtos = new List<HANodeDto>();

        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = managers.FirstOrDefault(s => s.SwarmNodeId == node.Id);

            int runningServices = 0;

            nodeDtos.Add(new HANodeDto(
                NodeId: node.Id,
                Hostname: node.Hostname,
                Role: node.Role,
                State: node.State,
                Availability: node.Availability,
                IsLeader: node.IsLeader,
                ServerName: server?.Name,
                ServerId: server?.Id,
                Region: server?.Region?.Name,
                Address: node.Address,
                NanoCPUs: node.NanoCPUs,
                MemoryBytes: node.MemoryBytes,
                EngineVersion: node.EngineVersion,
                RunningServices: runningServices,
                LastSeen: DateTime.UtcNow));
        }

        return Task.FromResult(nodeDtos);
    }

    private List<HARegionDto> BuildRegionDtos(List<HANodeDto> nodes)
    {
        var regionGroups = nodes.Where(n => !string.IsNullOrEmpty(n.Region)).GroupBy(n => n.Region!);
        var regionDtos = new List<HARegionDto>();

        foreach (var group in regionGroups)
        {
            var regionNodes = group.ToList();
            var managers = regionNodes.Where(n => n.Role.Equals("manager", StringComparison.OrdinalIgnoreCase)).ToList();
            var workers = regionNodes.Where(n => n.Role.Equals("worker", StringComparison.OrdinalIgnoreCase)).ToList();

            regionDtos.Add(new HARegionDto(
                Name: group.Key,
                ManagerCount: managers.Count,
                WorkerCount: workers.Count,
                OnlineManagers: managers.Count(m => m.State.Equals("ready", StringComparison.OrdinalIgnoreCase)),
                OnlineWorkers: workers.Count(w => w.State.Equals("ready", StringComparison.OrdinalIgnoreCase)),
                Nodes: regionNodes));
        }

        var noRegionNodes = nodes.Where(n => string.IsNullOrEmpty(n.Region)).ToList();
        if (noRegionNodes.Any())
        {
            var managers = noRegionNodes.Where(n => n.Role.Equals("manager", StringComparison.OrdinalIgnoreCase)).ToList();
            var workers = noRegionNodes.Where(n => n.Role.Equals("worker", StringComparison.OrdinalIgnoreCase)).ToList();

            regionDtos.Add(new HARegionDto(
                Name: "No Region",
                ManagerCount: managers.Count,
                WorkerCount: workers.Count,
                OnlineManagers: managers.Count(m => m.State.Equals("ready", StringComparison.OrdinalIgnoreCase)),
                OnlineWorkers: workers.Count(w => w.State.Equals("ready", StringComparison.OrdinalIgnoreCase)),
                Nodes: noRegionNodes));
        }

        return regionDtos;
    }

    private Task<List<HAServiceStatusDto>> BuildServiceDtos(List<ServiceInfo> services, CancellationToken cancellationToken)
    {
        var serviceDtos = new List<HAServiceStatusDto>();

        foreach (var service in services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int desiredReplicas = service.Replicas;
            int runningReplicas = service.Replicas;

            string status = "healthy";
            if (runningReplicas == 0)
                status = "critical";
            else if (runningReplicas < desiredReplicas)
                status = "degraded";

            bool isHAReady = desiredReplicas >= 2 && runningReplicas >= 2;
            var replicasByNode = new Dictionary<string, int>();

            serviceDtos.Add(new HAServiceStatusDto(
                ServiceId: service.Id,
                Name: service.Name,
                Image: service.Image,
                DesiredReplicas: desiredReplicas,
                RunningReplicas: runningReplicas,
                Status: status,
                IsHAReady: isHAReady,
                ReplicasByNode: replicasByNode,
                UpdatedAt: service.Created));
        }

        return Task.FromResult(serviceDtos);
    }

    private List<string> GenerateRecommendations(int onlineManagers, int totalWorkers, List<HAServiceStatusDto> services)
    {
        var recommendations = new List<string>();

        if (onlineManagers < 2)
        {
            recommendations.Add("🔴 CRITICAL: Less than 2 managers online. Cluster has no redundancy.");
        }
        else if (onlineManagers == 2)
        {
            recommendations.Add("⚠️ WARNING: Only 2 managers online. Add 1 more for optimal quorum (survives 1 failure).");
        }
        else if (onlineManagers == 3)
        {
            recommendations.Add("✅ Quorum healthy with 3 managers. Can survive 1 manager failure.");
        }

        if (totalWorkers == 0)
        {
            recommendations.Add("💡 Consider adding worker nodes for workload isolation from management plane.");
        }

        var traefikService = services.FirstOrDefault(s => s.Name.Contains("traefik", StringComparison.OrdinalIgnoreCase));
        if (traefikService != null && !traefikService.IsHAReady)
        {
            recommendations.Add("⚠️ Traefik reverse proxy not HA-ready. Scale to 2+ replicas for zero-downtime.");
        }

        var degradedServices = services.Where(s => s.Status == "degraded").ToList();
        if (degradedServices.Any())
        {
            recommendations.Add($"⚠️ {degradedServices.Count} service(s) running with reduced replicas. Check service health.");
        }

        var criticalServices = services.Where(s => s.Status == "critical").ToList();
        if (criticalServices.Any())
        {
            recommendations.Add($"🔴 {criticalServices.Count} service(s) completely down. Immediate attention required.");
        }

        if (!recommendations.Any())
        {
            recommendations.Add("✅ All systems healthy. Cluster operating optimally.");
        }

        return recommendations;
    }

    private static List<HAMetricPoint> AggregateHealthData(
        List<Core.Entities.HealthCheck> healthChecks,
        DateTime startTime,
        DateTime endTime,
        int intervalMinutes)
    {
        var points = new List<HAMetricPoint>();
        var currentTime = startTime;

        while (currentTime <= endTime)
        {
            var bucketEnd = currentTime.AddMinutes(intervalMinutes);
            var bucketChecks = healthChecks.Where(h => h.CheckedAt >= currentTime && h.CheckedAt < bucketEnd).ToList();
            var healthyServers = bucketChecks.Where(h => h.Status == HealthStatus.Healthy).Select(h => h.ServerId).Distinct().Count();

            points.Add(new HAMetricPoint(
                Timestamp: currentTime,
                Value: healthyServers,
                Label: null));

            currentTime = bucketEnd;
        }

        return points;
    }
}
