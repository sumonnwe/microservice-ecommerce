using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shared.OutboxPublisher.Models;

namespace Shared.OutboxPublisher.Abstractions
{
    /// <summary>
    /// Implement this interface in your persistence layer to provide
    /// acquiring/locking pending outbox entries and updating their status.
    /// </summary>
    public interface IOutboxStore
    {
        /// <summary>
        /// Acquire and lock a batch of pending entries for processing.
        /// The implementation should atomically mark entries as locked until now + lockDuration.
        /// </summary>
        Task<IReadOnlyList<OutboxEntry>> AcquireAndLockAsync(int batchSize, TimeSpan lockDuration, CancellationToken cancellationToken);

        /// <summary>
        /// Mark an entry as sent (success).
        /// </summary>
        Task MarkSentAsync(Guid id, CancellationToken cancellationToken);

        /// <summary>
        /// Mark an entry as failed; provide the next retry count and whether it is permanently failed.
        /// Implementation should update error information and retry count.
        /// </summary>
        Task MarkFailedAsync(Guid id, int retryCount, string error, bool permanentlyFailed, CancellationToken cancellationToken);
    }
}