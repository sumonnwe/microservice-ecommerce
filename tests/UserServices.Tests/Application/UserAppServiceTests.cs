using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Shared.Domain.Entities;
using Shared.Domain.Services;
using UserService.Application;
using UserService.Events;

namespace UserServices.Tests.Application
{
    [TestFixture]
    public class UserAppServiceTests
    {
        private Mock<IOutboxRepository> _outboxRepoMock = null!;
        private UserAppService _sut = null!;

        [SetUp]
        public void Setup()
        {
            _outboxRepoMock = new Mock<IOutboxRepository>();
            _sut = new UserAppService(_outboxRepoMock.Object);
        }

        [Test]
        public async Task CreateUserAsync_WithValidData_ShouldCreateUserAndOutboxEntry()
        {
            // Arrange
            var name = "Alice";
            var email = "alice@test.com";

            OutboxEntry? savedOutbox = null;

            _outboxRepoMock
                .Setup(r => r.AddAsync(It.IsAny<OutboxEntry>()))
                .Callback<OutboxEntry>(o => savedOutbox = o)
                .Returns(Task.CompletedTask);

            // Act
            var user = await _sut.CreateUserAsync(name, email);

            // Assert - User
            user.Should().NotBeNull();
            user.Id.Should().NotBeEmpty();
            user.Name.Should().Be(name);
            user.Email.Should().Be(email);

            // Assert - Outbox
            savedOutbox.Should().NotBeNull();
            savedOutbox!.EventType.Should().Be("users.created");
            savedOutbox.AggregateId.Should().Be(user.Id);
            savedOutbox.RetryCount.Should().Be(0);

            var evt = JsonSerializer.Deserialize<UserCreatedEvent>(savedOutbox.Payload);
            evt.Should().NotBeNull();
            evt!.Id.Should().Be(user.Id);
            evt.Name.Should().Be(name);
            evt.Email.Should().Be(email);

            _outboxRepoMock.Verify(r => r.AddAsync(It.IsAny<OutboxEntry>()), Times.Once);
        }

        [Test]
        public void CreateUserAsync_WithEmptyName_ShouldThrowArgumentException()
        {
            // Act
            Func<Task> act = () => _sut.CreateUserAsync("", "test@test.com");

            // Assert
            act.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage("*Name required*");
        }

        [Test]
        public void CreateUserAsync_WithEmptyEmail_ShouldThrowArgumentException()
        {
            // Act
            Func<Task> act = () => _sut.CreateUserAsync("Alice", "");

            // Assert
            act.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage("*Email required*");
        }

        [Test]
        public async Task CreateUserAsync_ShouldAlwaysWriteOutboxBeforeReturn()
        {
            // Arrange
            var called = false;

            _outboxRepoMock
                .Setup(r => r.AddAsync(It.IsAny<OutboxEntry>()))
                .Callback(() => called = true)
                .Returns(Task.CompletedTask);

            // Act
            await _sut.CreateUserAsync("Bob", "bob@test.com");

            // Assert
            called.Should().BeTrue();
        }
    }
}
