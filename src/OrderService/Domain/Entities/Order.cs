using System;
using Shared.Domain;

namespace OrderService.Domain.Entities
{
    public class Order
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Product { get; set; } = default!;
        public int Quantity { get; set; }
        public decimal Price { get; set; }

        // New: status and cancellation timestamp
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public DateTime? CancelledAtUtc { get; set; }
    }
}
