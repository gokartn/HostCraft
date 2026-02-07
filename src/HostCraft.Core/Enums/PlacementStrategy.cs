namespace HostCraft.Core.Enums;

/// <summary>
/// Strategy for distributing service replicas across Swarm nodes.
/// Controls how Docker Swarm places task replicas.
/// </summary>
public enum PlacementStrategy
{
    /// <summary>
    /// Spread replicas evenly across all available nodes (HA/DR recommended).
    /// Uses spread preference to distribute replicas.
    /// Best for: High availability, fault tolerance.
    /// </summary>
    Spread = 0,

    /// <summary>
    /// Pack replicas onto nodes until full, then move to next node.
    /// Maximizes resource utilization but reduces fault tolerance.
    /// Best for: Resource efficiency, cost optimization.
    /// </summary>
    Binpack = 1,

    /// <summary>
    /// No specific placement strategy - Swarm decides based on available resources.
    /// Uses default Swarm scheduler behavior.
    /// Best for: Simple deployments, no specific HA requirements.
    /// </summary>
    Random = 2,

    /// <summary>
    /// Use custom placement constraints defined in SwarmPlacementConstraints.
    /// Allows fine-grained control via node labels and constraints.
    /// Best for: Advanced use cases, specific node requirements.
    /// </summary>
    Custom = 3
}
