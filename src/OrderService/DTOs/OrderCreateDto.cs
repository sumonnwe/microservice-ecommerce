using System;

namespace OrderService.DTOs
{
    public class OrderCreateDto
    {
        public Guid UserId { get; set; }
        public string Product { get; set; } = default!;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }
}
