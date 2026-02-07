using System.Threading.Channels;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;

namespace HostCraft.Infrastructure.BackgroundJobs;

/// <summary>
/// In-memory queue for deployment jobs backed by an unbounded channel.
/// </summary>
public class DeploymentJobQueue : IDeploymentJobQueue
{
    private readonly Channel<DeploymentJob> _queue;

    public DeploymentJobQueue()
    {
        var options = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        };

        _queue = Channel.CreateUnbounded<DeploymentJob>(options);
    }

    public ValueTask EnqueueAsync(DeploymentJob job, CancellationToken cancellationToken = default)
    {
        if (!_queue.Writer.TryWrite(job))
        {
            return _queue.Writer.WriteAsync(job, cancellationToken);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<DeploymentJob> DequeueAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAsync(cancellationToken);
    }
}
