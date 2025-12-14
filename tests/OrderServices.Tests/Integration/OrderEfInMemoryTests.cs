using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using OrderService.Application;
using OrderService.Infrastructure.EF;
using Shared.Domain.Entities;

namespace OrderService.Tests.Integration;

[TestFixture]
public class OrderEfInMemoryTests
{
    [Test]
    public async Task CreateOrder_ShouldPersistOutbox()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new OrderDbContext(options);
        var sut = new OrderAppService((Shared.Domain.Services.IOutboxRepository)db);

        await sut.CreateOrderAsync(Guid.NewGuid(), "Keyboard", 1, 120);
        await db.SaveChangesAsync();

        var outbox = await db.Set<OutboxEntry>().SingleAsync();
        outbox.EventType.Should().Be("orders.created");
    }
}
