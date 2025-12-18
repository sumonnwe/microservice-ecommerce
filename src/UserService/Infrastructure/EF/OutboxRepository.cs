using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shared.Domain.Entities;
using Shared.Domain.Services;

namespace UserService.Infrastructure.EF
{
    // Implementation that manipulates Outbox table in the same DB
    public class OutboxRepository : IOutboxRepository
    {
        private readonly UserDbContext _db;
        public OutboxRepository(UserDbContext db) => _db = db;

        public async Task AddAsync(OutboxEntry entry)
        {
            // Add entry to DbContext; caller owns transaction around other domain entities
            _db.OutboxEntries.Add(entry);
            await _db.SaveChangesAsync();
        }

        public async Task<List<OutboxEntry>> GetUnsentAsync(int maxRetry = 5)
        {
            return await _db.OutboxEntries.Where(e => e.SentAt == null && e.RetryCount < maxRetry)
                                          .OrderBy(e => e.CreatedAt)
                                          .ToListAsync();
        }

        public async Task MarkAsSentAsync(Guid id)
        {
            var e = await _db.OutboxEntries.FindAsync(id);
            if (e == null) return;
            e.SentAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task IncrementRetryAsync(Guid id)
        {
            var e = await _db.OutboxEntries.FindAsync(id);
            if (e == null) return;
            e.RetryCount++;
            await _db.SaveChangesAsync();
        }
    }
}
