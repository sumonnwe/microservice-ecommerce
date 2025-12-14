using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Shared.Domain.Entities;
using UserService.Controllers;
using UserService.DTOs;
using UserService.Domain.Entities;
using UserService.Infrastructure.EF;
using NUnit.Framework.Legacy;

namespace UserServices.Tests.Integration
{
    [TestFixture]
    public class UserIntegrationTests
    {
        [Test]
        public async Task CreateUser_InMemoryDb_PersistsUserAndOutbox()
        {
            var options = new DbContextOptionsBuilder<UserDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new UserDbContext(options);
            var logger = new Mock<ILogger<UsersController>>();
            var controller = new UsersController(db, logger.Object);

            var dto = new UserCreateDto
            {
                Name = "Integration User",
                Email = "integration.user@example.com"
            };

            var result = await controller.Create(dto, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<CreatedAtActionResult>(), $"Unexpected response type: {result?.GetType().FullName ?? "<null>"}");
            var created = (CreatedAtActionResult)result;
            Assert.That(created.ActionName, Is.EqualTo(nameof(UsersController.GetById)));

            var returned = created.Value as User;
            Assert.That(returned, Is.Not.Null);
            Assert.That(returned!.Name, Is.EqualTo("Integration User"));
            Assert.That(returned.Email, Is.EqualTo("integration.user@example.com"));

            var persisted = await db.Users.FindAsync(returned.Id);
            Assert.That(persisted, Is.Not.Null);

            var outbox = db.OutboxEntries.SingleOrDefault(e => e.AggregateId == returned.Id);
            Assert.That(outbox, Is.Not.Null);
            Assert.That(outbox!.EventType, Is.EqualTo("users.created"));
            StringAssert.Contains("\"Email\":\"integration.user@example.com\"", outbox.Payload);
        }

        [Test]
        public async Task CreateUser_InvalidPayload_ReturnsBadRequest()
        {
            var options = new DbContextOptionsBuilder<UserDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new UserDbContext(options);
            var logger = new Mock<ILogger<UsersController>>();
            var controller = new UsersController(db, logger.Object);

            // Missing name and email -> validation failure
            var dto1 = new UserCreateDto { Name = "", Email = "" };
            var res1 = await controller.Create(dto1, CancellationToken.None);
            Assert.That(res1, Is.InstanceOf<ObjectResult>());
            var obj1 = (ObjectResult)res1;
            Assert.That(obj1.StatusCode, Is.EqualTo(400));

            // Malformed email -> validation failure
            var dto2 = new UserCreateDto { Name = "Name", Email = "not-an-email" };
            var res2 = await controller.Create(dto2, CancellationToken.None);
            Assert.That(res2, Is.InstanceOf<ObjectResult>());
            var obj2 = (ObjectResult)res2;
            Assert.That(obj2.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public async Task GetById_WhenExists_ReturnsOk()
        {
            var options = new DbContextOptionsBuilder<UserDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new UserDbContext(options);

            var user = new User
            {
                Id = Guid.NewGuid(),
                Name = "Seeded",
                Email = "seeded@example.com"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var logger = new Mock<ILogger<UsersController>>();
            var controller = new UsersController(db, logger.Object);

            var res = await controller.GetById(user.Id);

            Assert.That(res, Is.InstanceOf<OkObjectResult>());
            var ok = (OkObjectResult)res;
            var returned = ok.Value as User;
            Assert.That(returned, Is.Not.Null);
            Assert.That(returned!.Id, Is.EqualTo(user.Id));
            Assert.That(returned.Email, Is.EqualTo("seeded@example.com"));
        }
    }
}