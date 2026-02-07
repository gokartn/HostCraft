namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing deployment logs with real-time streaming.
/// </summary>
public interface IDeploymentLogService
{
    /// <summary>
    /// Add a log entry to a deployment and broadcast it to connected clients.
    /// </summary>
    /// <param name="deploymentId">The deployment ID</param>
    /// <param name="message">The log message</param>
    /// <param name="level">Log level (Info, Warning, Error, Success)</param>
    Task AddLogAsync(int deploymentId, string message, string level = "Info");

    /// <summary>
    /// Add multiple log entries efficiently.
    /// </summary>
    Task AddLogsAsync(int deploymentId, IEnumerable<(string message, string level)> logs);

    /// <summary>
    /// Create a progress reporter that automatically logs to this deployment.
    /// </summary>
    IProgress<string> CreateProgressReporter(int deploymentId, string level = "Info");
}
