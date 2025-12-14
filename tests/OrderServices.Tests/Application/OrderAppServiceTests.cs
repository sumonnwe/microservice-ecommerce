using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using OrderService.Application;
using OrderService.Events;
using Shared.Domain.Entities;
using Shared.Domain.Services;

namespace OrderService.Tests.Application;

[TestFixture]
public class OrderAppServiceTests
{
    private Mock<IOutboxRepository> _outboxRepo = null!;
    private OrderAppService _sut = null!;

    [SetUp]
    public void Setup()
    {
        _outboxRepo = new Mock<IOutboxRepository>();
        _sut = new OrderAppService(_outboxRepo.Object);
    }

    [Test]
    public async Task CreateOrderAsync_ValidInput_ShouldCreateOrder_AndOutbox()
    {
        // Arrange
        var userId = Guid.NewGuid();
        OutboxEntry? saved = null;

        _outboxRepo
            .Setup(r => r.AddAsync(It.IsAny<OutboxEntry>()))
            .Callback<OutboxEntry>(o => saved = o)
            .Returns(Task.CompletedTask);

        // Act
        var order = await _sut.CreateOrderAsync(userId, "Laptop", 2, 2000);

        // Assert
        order.Should().NotBeNull();
        order.UserId.Should().Be(userId);
        order.Product.Should().Be("Laptop");

        saved.Should().NotBeNull();
        saved!.EventType.Should().Be("orders.created");

        var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(saved.Payload);
        evt!.Product.Should().Be("Laptop");
        evt.Quantity.Should().Be(2);
    }

    [Test]
    public void CreateOrderAsync_InvalidQuantity_ShouldThrow()
    {
        Func<Task> act = () => _sut.CreateOrderAsync(Guid.NewGuid(), "Item", -1, 10);
        act.Should().ThrowAsync<ArgumentException>().WithMessage("*Quantity*");
    }

    [Test]
    public void CreateOrderAsync_InvalidPrice_ShouldThrow()
    {
        Func<Task> act = () => _sut.CreateOrderAsync(Guid.NewGuid(), "Item", 1, 0m);
        act.Should().ThrowAsync<ArgumentException>().WithMessage("*Price*");
    }

    [Test]
    public void CreateOrderAsync_NullOrEmptyProduct_ShouldThrow()
    {
        Func<Task> act1 = () => _sut.CreateOrderAsync(Guid.NewGuid(), "", 1, 10m);
        Func<Task> act2 = () => _sut.CreateOrderAsync(Guid.NewGuid(), (string?)null!, 1, 10m);

        act1.Should().ThrowAsync<ArgumentException>().WithMessage("*product*");
        act2.Should().ThrowAsync<ArgumentException>().WithMessage("*product*");
    }

    [Test]
    public void CreateOrderAsync_WhenOutboxRepositoryThrows_ShouldPropagate()
    {
        // Arrange
        _outboxRepo
            .Setup(r => r.AddAsync(It.IsAny<OutboxEntry>()))
            .ThrowsAsync(new InvalidOperationException("outbox failure"));

        // Act
        Func<Task> act = () => _sut.CreateOrderAsync(Guid.NewGuid(), "Widget", 1, 9.99m);

        // Assert
        act.Should().ThrowAsync<InvalidOperationException>().WithMessage("outbox failure");
    }
}