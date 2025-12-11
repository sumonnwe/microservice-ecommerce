using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shared.Domain.Entities;
using Shared.Domain.Services;

namespace OrderService.Infrastructure.EF
{
    public class OutboxRepository : IOutboxRepository
    {
        private readonly OrderDbContext _db;
        public OutboxRepository(OrderDbContext db) => _db = db;

        public async Task AddAsync(OutboxEntry entry)
        {
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
