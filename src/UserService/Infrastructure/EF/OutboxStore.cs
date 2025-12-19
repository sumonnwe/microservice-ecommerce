using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.OutboxPublisher.Abstractions;
using Shared.OutboxPublisher.Models;
using UserService.Infrastructure.EF;
using Shared.OutboxPublisher;

namespace UserService.Infrastructure.EF
{
    /// <summary>
    /// EF-backed adapter for UserService that implements IOutboxStore expected by the shared outbox publisher.
    /// Same note as OrderService.OutboxStore about locking — add a lock column for robust multi-consumer scenarios.
    /// </summary>
    public class OutboxStore : IOutboxStore
    {
        private readonly UserDbContext _db;
        private readonly OutboxPublisherOptions _options;

        public OutboxStore(UserDbContext db, IOptions<OutboxPublisherOptions> options)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<IReadOnlyList<Shared.OutboxPublisher.Models.OutboxEntry>> AcquireAndLockAsync(int batchSize, TimeSpan lockDuration, CancellationToken cancellationToken)
        {
            if (batchSize <= 0) batchSize = 1;

            var maxRetries = Math.Max(1, _options.MaxRetries);
            var entries = await _db.OutboxEntries
                                   .Where(e => e.SentAt == null && e.RetryCount < maxRetries)
                                   .OrderBy(e => e.CreatedAt)
                                   .Take(batchSize)
                                   .AsNoTracking()
                                   .ToListAsync(cancellationToken)
                                   .ConfigureAwait(false);

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
                e.SentAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}