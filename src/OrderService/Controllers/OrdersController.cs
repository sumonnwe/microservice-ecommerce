using Microsoft.AspNetCore.Mvc;
using OrderService.Infrastructure.EF;
using OrderService.Domain.Entities;
using OrderService.DTOs;
using Shared.Domain.Entities;
using System.Text.Json;

namespace OrderService.Controllers
{
    [ApiController]
    [Route("api/orders")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderDbContext _db;

        public OrdersController(OrderDbContext db)
        {
            _db = db; 
        }

        [HttpPost] 
        public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
        {
            if (dto.Quantity <= 0) return BadRequest("Quantity must be > 0");
            if (dto.Price <= 0) return BadRequest("Price must be > 0");
            
            var evt = new {
                Id = Guid.NewGuid(), UserId = dto.UserId, Product = dto.Product, Quantity = dto.Quantity, Price = dto.Price };

            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = dto.UserId,
                Product = dto.Product,
                Quantity = dto.Quantity,
                Price = dto.Price
            };

            var outbox = new OutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "orders.created",
                AggregateId = order.Id,
                Payload = JsonSerializer.Serialize(evt),
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            // Single DbContext -> single transaction when SaveChangesAsync is called.
            _db.Orders.Add(order);
            _db.OutboxEntries.Add(outbox);
            await _db.SaveChangesAsync();
            //await tx.CommitAsync();

            return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var order = await _db.Orders.FindAsync(id);
            if (order == null) return NotFound();
            return Ok(order);
        }
    }
}
