using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared.Domain.Entities;

namespace Shared.Domain.Services
{
    public interface IOutboxRepository
    {
        Task AddAsync(OutboxEntry entry);
        Task<List<OutboxEntry>> GetUnsentAsync(int maxRetry = 5);
        Task MarkAsSentAsync(Guid id);
        Task IncrementRetryAsync(Guid id);
    }
}
