using System;

namespace OrderService.Events
{
    public class OrderCreatedEvent
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Product { get; set; } = default!;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }
}
