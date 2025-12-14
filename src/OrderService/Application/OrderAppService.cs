using System;
using System.Text.Json;
using System.Threading.Tasks;
using Shared.Domain.Entities;
using Shared.Domain.Services;
using OrderService.Domain.Entities;
using OrderService.Events;

namespace OrderService.Application
{
    // Domain logic: validate and create order; create OutboxEntry in same DB transaction
    public class OrderAppService
    {
        private readonly IOutboxRepository _outboxRepository;

        public OrderAppService(IOutboxRepository outboxRepository)
        {
            _outboxRepository = outboxRepository;
        }

        public async Task<Order> CreateOrderAsync(Guid userId, string product, int quantity, decimal price)
        {
            if (quantity < 0) throw new ArgumentException("Quantity is wrong", nameof(quantity));
            if (string.IsNullOrWhiteSpace(product)) throw new ArgumentException("Product required", nameof(product));

            var order = new Order { Id = Guid.NewGuid(), UserId = userId, Product = product, Quantity = quantity, Price = price };

            var evt = new OrderCreatedEvent { Id = Guid.NewGuid(), UserId = userId, Product = product, Quantity = quantity, Price = price };
            var outbox = new OutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "orders.created",
                AggregateId = order.Id,
                Payload = JsonSerializer.Serialize(evt),
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            // The persistence + outbox write occurs in Infrastructure via a single DbContext SaveChanges in controller
            await _outboxRepository.AddAsync(outbox);

            return order;
        }
    }
}
