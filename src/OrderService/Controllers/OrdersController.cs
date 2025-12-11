using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using OrderService.DTOs;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.EF;
using Microsoft.EntityFrameworkCore;
using Shared.Domain.Entities;
using Shared.Domain.Services;
using System.Text.Json;
using OrderService.Events;

namespace OrderService.Controllers
{
    [ApiController]
    [Route("api/orders")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderDbContext _db;
        private readonly IOutboxRepository _outboxRepo;
        public OrdersController(OrderDbContext db, IOutboxRepository outboxRepo)
        {
            _db = db;
            _outboxRepo = outboxRepo;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
        {
            if (dto.Quantity <= 0) return BadRequest("Quantity must be > 0");
            if (dto.Price <= 0) return BadRequest("Price must be > 0");

            await using var tx = await _db.Database.BeginTransactionAsync();

            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = dto.UserId,
                Product = dto.Product,
                Quantity = dto.Quantity,
                Price = dto.Price
            };

            _db.Orders.Add(order);

            var evt = new OrderCreatedEvent
            {
                Id = order.Id,
                UserId = order.UserId,
                Product = order.Product,
                Quantity = order.Quantity,
                Price = order.Price
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

            _db.OutboxEntries.Add(outbox);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

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
