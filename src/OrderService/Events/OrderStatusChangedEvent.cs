using System;
using OrderService.Domain.Entities;
using Shared.Domain;

namespace OrderService.Events
{
    public class OrderStatusChangedEvent
    {
        public Guid EventId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public Guid OrderId { get; set; }
        public OrderStatus OldStatus { get; set; }
        public OrderStatus NewStatus { get; set; }
        public string? Reason { get; set; }

        // Keep an explicit topic name so callers can set OutboxEntry.EventType consistently
        public string EventType => "orders.status-changed";
    }
}