using System; 
namespace Shared.Domain.Entities
{
    // Shared outbox entity used by both UserService and OrderService.
    public class OutboxEntry
    {
        public Guid Id { get; set; }
        public string EventType { get; set; } = null!;
        public Guid AggregateId { get; set; }
        public string Payload { get; set; } = null!;
        public int RetryCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }
}
