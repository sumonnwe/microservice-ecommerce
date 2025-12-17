using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using OrderService.BackgroundServices;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.EF;
using Shared.Domain.Entities;

namespace OrderService.Tests.Background
{
    [TestFixture]
    public class OrderExpiryWorkerTests
    {
        [Test] 
        public async Task ExpiryWorker_MarksExpired_And_CreatesOutboxEntry()
        {
            // Arrange - service collection with InMemory DB
            var services = new ServiceCollection();
            var dbName = $"OrderExpiryTest_{Guid.NewGuid():N}";

            services.AddLogging(); // required for scope/provider resolution if needed
            services.AddDbContext<OrderDbContext>(opts => opts.UseInMemoryDatabase(dbName));

            var sp = services.BuildServiceProvider();

            // Seed an order that already expired
            var orderId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

                var now = DateTime.UtcNow;
                var expiredOrder = new Order
                {
                    Id = orderId,
                    UserId = userId,
                    Product = "Test",
                    Quantity = 1,
                    Price = 1m,
                    Status = OrderStatus.PendingPayment,
                    CreatedAtUtc = now.AddMinutes(-30),
                    ExpiresAtUtc = now.AddMinutes(-15)
                };

                db.Orders.Add(expiredOrder);
                await db.SaveChangesAsync();
            }

            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var loggerMock = new Mock<ILogger<OrderExpiryWorker>>();
            var worker = new OrderExpiryWorker(scopeFactory, loggerMock.Object);

            // Act - run the worker once (allow it to process and then cancel)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await worker.ExecuteAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // expected when token expires; swallow to continue assertions
            }

            // Assert - order status updated and outbox entry created
            using (var assertScope = sp.CreateScope())
            {
                var db = assertScope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var persisted = await db.Orders.FindAsync(orderId);
                Assert.That(persisted, Is.Not.Null, "Order should still exist");
                Assert.That(persisted!.Status, Is.EqualTo(OrderStatus.Expired), "Order status should be Expired");

                var outbox = db.OutboxEntries.SingleOrDefault(e => e.AggregateId == orderId);
                Assert.That(outbox, Is.Not.Null, "An OutboxEntry should have been created for the expired order");
                Assert.That(outbox!.EventType, Is.EqualTo("orders.cancelled"), "Outbox EventType must be orders.cancelled");

                // payload should contain reason = "timeout"
                StringAssert.Contains("\"reason\":\"timeout\"", outbox.Payload.Replace(" ", ""), "Outbox payload must include reason=\"timeout\"");
            }
        }
    }
}
