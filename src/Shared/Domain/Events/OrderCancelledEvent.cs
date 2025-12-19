using System;

namespace Shared.Domain.Events
{
    // Integration event published when an order is cancelled.
    public class OrderCancelledEvent
    {
        public Guid EventId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public string Reason { get; set; } = default!;

        // Useful for routing / topic identification in outbox entries.
        public string EventType { get; } = "orders.cancelled";
    }
}
