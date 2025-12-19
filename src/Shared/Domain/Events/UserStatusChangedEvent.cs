using System;

namespace Shared.Domain.Events
{
    // Integration event published when a user's status changes.
    public class UserStatusChangedEvent
    {
        public Guid EventId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public Guid UserId { get; set; }
        public Shared.Domain.UserStatus OldStatus { get; set; }
        public Shared.Domain.UserStatus NewStatus { get; set; }
        public string? Reason { get; set; }

        // Useful for routing / topic identification in outbox entries.
        public string EventType { get; } = "users.status-changed";
    }
}
