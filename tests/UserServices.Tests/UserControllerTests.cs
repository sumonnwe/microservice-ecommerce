using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UserService.Controllers;
using UserService.Domain.Entities;
using UserService.DTOs;
using UserService.Infrastructure.EF;

namespace UserService.Tests
{
    public class UsersControllerTests
    {
        [Test]
        public async Task Create_Valid_ReturnsCreated_And_PersistsOutboxEntry()
        {
            var options = new DbContextOptionsBuilder<UserDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new UserDbContext(options);
            var loggerMock = new Mock<ILogger<UsersController>>();
            var controller = new UsersController(db, loggerMock.Object);

            var dto = new UserCreateDto { Name = "abc", Email = "x@y.com" };

            var res = await controller.Create(dto, CancellationToken.None);

            Assert.That(res, Is.InstanceOf<CreatedAtActionResult>(), $"Unexpected response type: {res?.GetType().FullName ?? "<null>"}");
            var created = (CreatedAtActionResult)res;
            Assert.That(created.ActionName, Is.EqualTo(nameof(UsersController.GetById)));

            // returned user
            var returned = created.Value as User;
            Assert.That(returned, Is.Not.Null, "CreatedAtActionResult.Value was not a User");
            Assert.That(returned!.Name, Is.EqualTo("abc"));
            Assert.That(returned.Email, Is.EqualTo("x@y.com"));

            // persisted to DB: user and outbox entry
            var persistedUser = await db.Users.FindAsync(returned.Id);
            Assert.That(persistedUser, Is.Not.Null);

            var outbox = db.OutboxEntries.SingleOrDefault(e => e.AggregateId == returned.Id);
            Assert.That(outbox, Is.Not.Null);
            Assert.That(outbox!.EventType, Is.EqualTo("users.created"));
            StringAssert.Contains("\"Email\":\"x@y.com\"", outbox.Payload);
        }

        [Test]
        public async Task Create_DuplicateEmail_ReturnsConflict()
        {
            var options = new DbContextOptionsBuilder<UserDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new UserDbContext(options);

            // seed existing user
            var existing = new User { Id = Guid.NewGuid(), Name = "seed", Email = "dup@example.com" };
            db.Users.Add(existing);
            await db.SaveChangesAsync();

            var loggerMock = new Mock<ILogger<UsersController>>();
            var controller = new UsersController(db, loggerMock.Object);

            var dto = new UserCreateDto { Name = "new", Email = "dup@example.com" };
            var res = await controller.Create(dto, CancellationToken.None);

            // Controller returns Conflict(ProblemDetails) -> ObjectResult with StatusCode 409
            Assert.That(res, Is.InstanceOf<ObjectResult>(), $"Unexpected response type: {res?.GetType().FullName ?? "<null>"}");
            var obj = (ObjectResult)res;
            Assert.That(obj.StatusCode, Is.EqualTo(409));
        }

        [Test]
        public async Task Create_InvalidModel_ReturnsBadRequest()
        {
            var options = new DbContextOptionsBuilder<UserDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new UserDbContext(options);
            var loggerMock = new Mock<ILogger<UsersController>>();
            var controller = new UsersController(db, loggerMock.Object);

            // invalid payload (missing name and email)
            var dto = new UserCreateDto { Name = "", Email = "" };
            var res = await controller.Create(dto, CancellationToken.None);

            // ValidationProblem produces a 400 response
            Assert.That(res, Is.InstanceOf<ObjectResult>(), $"Unexpected response type: {res?.GetType().FullName ?? "<null>"}");
            var obj = (ObjectResult)res;
            Assert.That(obj.StatusCode, Is.EqualTo(400));
        }
    }
}