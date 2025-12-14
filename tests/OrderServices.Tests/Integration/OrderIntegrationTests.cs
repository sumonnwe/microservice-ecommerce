using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using OrderService.Controllers;
using OrderService.DTOs;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.EF;
using Shared.Domain.Entities;

namespace OrderService.Tests.Integration;

[TestFixture]
public class OrderIntegrationTests
{
    [Test]
    public async Task CreateOrder_InMemoryDb_PersistsOrderAndOutbox()
    {
        // Arrange - create a fresh in-memory database per test
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new OrderDbContext(options);
        var logger = new Mock<ILogger<OrdersController>>();
        var controller = new OrdersController(db, logger.Object);

        var dto = new OrderCreateDto
        {
            UserId = Guid.NewGuid(),
            Product = "Test Product",
            Quantity = 2,
            Price = 19.99m
        };

        // Act
        var result = await controller.Create(dto, CancellationToken.None);

        // Assert - controller response
        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>(), $"Unexpected response type: {result?.GetType().FullName ?? "<null>"}");
        var created = (CreatedAtActionResult)result;
        Assert.That(created.ActionName, Is.EqualTo(nameof(OrdersController.Get)));

        var returned = created.Value as Order;
        Assert.That(returned, Is.Not.Null);
        Assert.That(returned!.Product, Is.EqualTo("Test Product"));
        Assert.That(returned.Quantity, Is.EqualTo(2));
        Assert.That(returned.Price, Is.EqualTo(19.99m));

        // Assert - persisted in DB
        var persisted = await db.Orders.FindAsync(returned.Id);
        Assert.That(persisted, Is.Not.Null);

        var outbox = db.OutboxEntries.SingleOrDefault(e => e.AggregateId == returned.Id);
        Assert.That(outbox, Is.Not.Null);
        Assert.That(outbox!.EventType, Is.EqualTo("orders.created"));
        StringAssert.Contains("\"Product\":\"Test Product\"", outbox.Payload);
    }

    [Test]
    public async Task CreateOrder_InvalidPayload_ReturnsBadRequest()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new OrderDbContext(options);
        var logger = new Mock<ILogger<OrdersController>>();
        var controller = new OrdersController(db, logger.Object);

        // Quantity invalid
        var dto1 = new OrderCreateDto { UserId = Guid.NewGuid(), Product = "X", Quantity = 0, Price = 10m };
        var res1 = await controller.Create(dto1, CancellationToken.None);
        Assert.That(res1, Is.InstanceOf<ObjectResult>());
        var obj1 = (ObjectResult)res1;
        Assert.That(obj1.StatusCode, Is.EqualTo(400));

        // Price invalid
        var dto2 = new OrderCreateDto { UserId = Guid.NewGuid(), Product = "X", Quantity = 1, Price = 0m };
        var res2 = await controller.Create(dto2, CancellationToken.None);
        Assert.That(res2, Is.InstanceOf<ObjectResult>());
        var obj2 = (ObjectResult)res2;
        Assert.That(obj2.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task GetOrder_WhenExists_ReturnsOk()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new OrderDbContext(options);

        // seed an order
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Product = "Seeded",
            Quantity = 1,
            Price = 5.5m
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var logger = new Mock<ILogger<OrdersController>>();
        var controller = new OrdersController(db, logger.Object);

        var res = await controller.Get(order.Id);

        Assert.That(res, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)res;
        var returned = ok.Value as Order;
        Assert.That(returned, Is.Not.Null);
        Assert.That(returned!.Id, Is.EqualTo(order.Id));
        Assert.That(returned.Product, Is.EqualTo("Seeded"));
    }
}