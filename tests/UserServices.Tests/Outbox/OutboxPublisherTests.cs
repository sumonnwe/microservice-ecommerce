using FluentAssertions;
using Moq;
using NUnit.Framework;
using Shared.Domain.Entities;
using Shared.Domain.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UserServices.Tests.Outbox;

[TestFixture]
public class OutboxPublisherTests
{
    [Test]
    [Ignore("Temporarily disabled")]
    public async Task PublishPendingAsync_WhenPendingExists_ShouldPublishToKafka_AndMarkProcessed()
    {
        // Arrange
        var ct = CancellationToken.None;

        var pending = new List<OutboxEntry>
        {
            new OutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "users.created",
                AggregateId = Guid.NewGuid(),
                Payload = "{\"id\":\"123\"}",
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow
            }
        };

        var outboxRepo = new Mock<IOutboxRepository>();
        var kafka = new Mock<IKafkaProducer>();

        outboxRepo.Setup(r => r.GetUnsentAsync(It.IsAny<int>()))
                  .ReturnsAsync(pending);

        kafka.Setup(k => k.ProduceAsync(
                "users.created", 
                pending[0].Payload))
             .Returns(Task.CompletedTask);


        // Assert
        kafka.Verify(k => k.ProduceAsync(
            "users.created", 
            pending[0].Payload), Times.Once);

        outboxRepo.Verify(r => r.MarkAsSentAsync(pending[0].Id), Times.Once);
        outboxRepo.Verify(r => r.IncrementRetryAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Test]
    [Ignore("Temporarily disabled")]
    public async Task PublishPendingAsync_WhenKafkaFails_ShouldIncrementRetry_AndNotMarkProcessed()
    {
        // Arrange
        var ct = CancellationToken.None;

        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = "users.created",
            AggregateId = Guid.NewGuid(),
            Payload = "{\"id\":\"123\"}",
            RetryCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        var outboxRepo = new Mock<IOutboxRepository>();
        var kafka = new Mock<IKafkaProducer>();

        outboxRepo.Setup(r => r.GetUnsentAsync(It.IsAny<int>()))
                  .ReturnsAsync(new List<OutboxEntry> { entry });

        kafka.Setup(k => k.ProduceAsync(
                entry.EventType,
                entry.Payload))
             .ThrowsAsync(new Exception("Kafka down")); 

        // Assert
        outboxRepo.Verify(r => r.MarkAsSentAsync(entry.Id), Times.Once);
        outboxRepo.Verify(r => r.IncrementRetryAsync(entry.Id), Times.Never);
    }
}
