using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shared.Domain.Entities;
using System;
using UserService.Application;
using UserService.Infrastructure;
using UserService.Infrastructure.EF;

namespace UserServices.Tests.Integration;

[TestFixture]
public class UserEfInMemoryTests
{
    private UserDbContext _db = null!;
    private UserAppService _sut = null!;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new UserDbContext(options);
        _sut = new UserAppService((Shared.Domain.Services.IOutboxRepository)_db); // DbContext implements IOutboxRepository
    }

    [Test]
    public async Task CreateUser_ShouldPersistOutboxEntry()
    {
        // Act
        var user = await _sut.CreateUserAsync("Bob", "bob@test.com");
        await _db.SaveChangesAsync();

        // Assert
        var outbox = await _db.Set<OutboxEntry>().SingleAsync();
        outbox.EventType.Should().Be("users.created");
        outbox.AggregateId.Should().Be(user.Id);
        outbox.Payload.Should().NotBeNullOrEmpty();
    }
}
