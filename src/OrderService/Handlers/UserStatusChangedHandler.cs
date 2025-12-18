using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Domain.Events;
using Shared.Domain.Entities;
using OrderService.Infrastructure.EF;
using OrderService.Domain.Entities;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OrderService.Handlers
{
    /// <summary>
    /// Encapsulates the logic to handle UserStatusChangedEvent.
    /// This class is unit-testable (no direct Kafka dependency).
    /// Behavior:
    /// - If NewStatus != Inactive -> ignore
    /// - If NewStatus == Inactive -> cancel all Pending orders for that user (set status Cancelled and CancelledAtUtc)
    /// - For each cancelled order create an OutboxEntry with EventType "orders.cancelled" and OrderCancelledEvent payload.
    ///
    /// Decision: Emit one OrderCancelledEvent per cancelled order (rather than a batch event).
    /// Rationale: per-order events simplify downstream processing and tracing; consumers can process each cancellation independently.
    /// </summary>
    public class UserStatusChangedHandler
    {
        private readonly OrderDbContext _db;
        private readonly ILogger<UserStatusChangedHandler> _logger;

        public UserStatusChangedHandler(OrderDbContext db, ILogger<UserStatusChangedHandler> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task HandleAsync(UserStatusChangedEvent evt, CancellationToken cancellationToken)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            if (evt.NewStatus != Shared.Domain.UserStatus.Inactive)
            {
                _logger.LogDebug("UserStatusChangedHandler: ignoring event for user {UserId} newStatus={Status}", evt.UserId, evt.NewStatus);
                return;
            }

            _logger.LogInformation("Handling UserStatusChangedEvent: cancelling pending orders for user {UserId}", evt.UserId);

            // Find pending orders for user
            var pendingOrders = await _db.Orders
                .Where(o => o.UserId == evt.UserId && o.Status == OrderStatus.Pending)
                .ToListAsync(cancellationToken);

            if (!pendingOrders.Any())
            {
                _logger.LogInformation("No pending orders to cancel for user {UserId}", evt.UserId);
                return;
            }

            foreach (var order in pendingOrders)
            {
                // Idempotency: only cancel if still Pending
                if (order.Status != OrderStatus.Pending) continue;

                order.Status = OrderStatus.Cancelled;
                order.CancelledAtUtc = DateTime.UtcNow;

                // Create integration event payload
                var cancelledEvt = new OrderCancelledEvent
                {
                    EventId = Guid.NewGuid(),
                    OccurredAtUtc = DateTime.UtcNow,
                    OrderId = order.Id,
                    UserId = order.UserId,
                    Reason = evt.Reason ?? "user_inactivated"
                };

                var outbox = new OutboxEntry
                {
                    Id = Guid.NewGuid(),
                    EventType = cancelledEvt.EventType, // "orders.cancelled"
                    AggregateId = order.Id,
                    Payload = JsonSerializer.Serialize(cancelledEvt),
                    RetryCount = 0,
                    CreatedAt = DateTime.UtcNow
                };

                _db.OutboxEntries.Add(outbox);

                _logger.LogInformation("Order {OrderId} marked cancelled and outbox event created.", order.Id);
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving cancelled orders/outbox entries for User {UserId}", evt.UserId);
                throw;
            }
        }
    }
}
