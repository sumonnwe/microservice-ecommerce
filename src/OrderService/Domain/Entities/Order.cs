using System;

namespace OrderService.Domain.Entities
{
    public class Order
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Product { get; set; } = default!;
        public int Quantity { get; set; }
        public decimal Price { get; set; }

        // Domain status with clear states for payment & lifecycle
        public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;

        // Timestamps for TTL / inactivity cancellation
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(15);

        // Concurrency token
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }

    public enum OrderStatus
    {
        PendingPayment = 0,
        Ready = 1,
        Confirmed = 2,
        Cancelled = 3,
        Expired = 4
    }
}
