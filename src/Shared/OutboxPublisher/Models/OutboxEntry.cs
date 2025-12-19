using System;

namespace Shared.OutboxPublisher.Models
{
    public sealed class OutboxEntry
    {
        public Guid Id { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
        public int RetryCount { get; init; }
        public DateTime OccurredAt { get; init; }
    }
}