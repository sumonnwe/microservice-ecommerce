using System;

namespace Shared.Domain.Entities
{
    public class OutboxEntry
    {
        public Guid Id { get; set; }
        public string EventType { get; set; } = default!;
        public Guid AggregateId { get; set; }
        public string Payload { get; set; } = default!;
        public int RetryCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }
}
