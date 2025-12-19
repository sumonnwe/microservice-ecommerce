using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.OutboxPublisher.Abstractions;
using Shared.OutboxPublisher.Models;
using OrderService.Infrastructure.EF;
using Shared.OutboxPublisher;

namespace OrderService.Infrastructure.EF
{
    /// <summary>
    /// EF-backed adapter that implements the shared library IOutboxStore
    /// by mapping the existing Shared.Domain.Entities.OutboxEntry table.
    /// Note: the existing schema does not contain a lock column; AcquireAndLockAsync
    /// returns a snapshot of unsent rows. This is sufficient for many deployments
    /// but can cause duplicate work if multiple dispatchers run against the same DB.
    /// To get true DB-level locking add a LockedUntil/LockId column and update this class.
    /// </summary>
    public class OutboxStore : IOutboxStore
    {
        private readonly OrderDbContext _db;
        private readonly OutboxPublisherOptions _options;

        public OutboxStore(OrderDbContext db, IOptions<OutboxPublisherOptions> options)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<IReadOnlyList<Shared.OutboxPublisher.Models.OutboxEntry>> AcquireAndLockAsync(int batchSize, TimeSpan lockDuration, CancellationToken cancellationToken)
        {
            if (batchSize <= 0) batchSize = 1;

            // Select unsent entries (SentAt == null) and below retry threshold
            var maxRetries = Math.Max(1, _options.MaxRetries);
            var entries = await _db.OutboxEntries
                                   .Where(e => e.SentAt == null && e.RetryCount < maxRetries)
                                   .OrderBy(e => e.CreatedAt)
                                   .Take(batchSize)
                                   .AsNoTracking()
                                   .ToListAsync(cancellationToken)
                                   .ConfigureAwait(false);

            // Map to library model
            var mapped = entries.Select(e => new Shared.OutboxPublisher.Models.OutboxEntry
            {
                Id = e.Id,
                EventType = e.EventType,
                Payload = e.Payload,
                RetryCount = e.RetryCount,
                OccurredAt = e.CreatedAt
            }).ToList();

            return mapped;
        }

        public async Task MarkSentAsync(Guid id, CancellationToken cancellationToken)
        {
            var e = await _db.OutboxEntries.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
            if (e == null) return;

            e.SentAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task MarkFailedAsync(Guid id, int retryCount, string error, bool permanentlyFailed, CancellationToken cancellationToken)
        {
            var e = await _db.OutboxEntries.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
            if (e == null) return;

            e.RetryCount = retryCount;

            if (permanentlyFailed)
            {
                // No error column in current schema; mark SentAt to avoid further retries
                e.SentAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}