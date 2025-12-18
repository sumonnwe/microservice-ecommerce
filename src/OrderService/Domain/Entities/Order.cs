using System;

namespace OrderService.Domain.Entities
{
    public enum OrderStatus
    {
        Pending = 0,
        Completed = 1,
        Paid = 2,
        Cancelled = 3
    }

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
