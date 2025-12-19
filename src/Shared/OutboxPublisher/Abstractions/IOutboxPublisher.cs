using System.Threading;
using System.Threading.Tasks;
using Shared.OutboxPublisher.Models;

namespace Shared.OutboxPublisher.Abstractions
{
    public sealed class PublishResult
    {
        public bool Success { get; init; }
        public bool PermanentlyFailed { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>
    /// Publishes an OutboxEntry to its destination (e.g. Kafka).
    /// Implementations should not update the store; they only attempt to publish.
    /// </summary>
    public interface IOutboxPublisher
    {
        Task<PublishResult> PublishAsync(OutboxEntry entry, CancellationToken cancellationToken);
    }
}