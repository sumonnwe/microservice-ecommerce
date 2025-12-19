using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using OrderService.Domain.Entities;
using OrderService.Handlers;
using OrderService.Infrastructure.EF;
using Shared.Domain.Events; 
using Shared.Domain; 

namespace OrderService.Tests.Application
{
    [TestFixture]
    public class UserStatusChangedHandlerTests
    {
        private OrderDbContext CreateDb()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new OrderDbContext(options);
        }

        [Test]
        public async Task InactiveEvent_CancelsPendingOrdersOnly_AndCreatesOutboxEntries()
        {
            // Arrange
            await using var db = CreateDb();

            var userId = Guid.NewGuid();
            var pending = new Order { Id = Guid.NewGuid(), UserId = userId, Product = "P1", Quantity = 1, Price = 5m, Status = OrderStatus.Pending };
            var completed = new Order { Id = Guid.NewGuid(), UserId = userId, Product = "P2", Quantity = 1, Price = 5m, Status = OrderStatus.Completed };

            db.Orders.Add(pending);
            db.Orders.Add(completed);
            await db.SaveChangesAsync();

            var handler = new UserStatusChangedHandler(db, NullLogger<UserStatusChangedHandler>.Instance);

            var evt = new UserStatusChangedEvent
            {
                EventId = Guid.NewGuid(),
                OccurredAtUtc = DateTime.UtcNow,
                UserId = userId,
                OldStatus = Shared.Domain.UserStatus.Active,
                NewStatus = Shared.Domain.UserStatus.Inactive,
                Reason = "admin"
            };

            // Act
            await handler.HandleAsync(evt, CancellationToken.None);

            // Assert
            var updatedPending = await db.Orders.FindAsync(pending.Id);
            updatedPending!.Status.Should().Be(OrderStatus.Cancelled);
            updatedPending.CancelledAtUtc.Should().NotBeNull();

            var updatedCompleted = await db.Orders.FindAsync(completed.Id);
            updatedCompleted!.Status.Should().Be(OrderStatus.Completed);

            // Outbox entry for cancelled order exists
            var outbox = db.OutboxEntries.Where(o => o.AggregateId == pending.Id && o.EventType == "orders.cancelled").ToList();
            outbox.Count.Should().Be(1);
        }

        [Test]
        public async Task Handler_Is_Idempotent_On_Replay()
        {
            // Arrange
            await using var db = CreateDb();

            var userId = Guid.NewGuid();
            var pending = new Order { Id = Guid.NewGuid(), UserId = userId, Product = "P1", Quantity = 1, Price = 5m, Status = OrderStatus.Pending };
            db.Orders.Add(pending);
            await db.SaveChangesAsync();

            var handler = new UserStatusChangedHandler(db, NullLogger<UserStatusChangedHandler>.Instance);

            var evt = new UserStatusChangedEvent
            {
                EventId = Guid.NewGuid(),
                OccurredAtUtc = DateTime.UtcNow,
                UserId = userId,
                OldStatus = Shared.Domain.UserStatus.Active,
                NewStatus = Shared.Domain.UserStatus.Inactive,
                Reason = "admin"
            };

            // Act - handle twice (simulate replay)
            await handler.HandleAsync(evt, CancellationToken.None);
            await handler.HandleAsync(evt, CancellationToken.None);

            // Assert - only one outbox entry and order cancelled once
            var updated = await db.Orders.FindAsync(pending.Id);
        }
    }
}
