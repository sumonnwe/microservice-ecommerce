using NUnit.Framework;
using Moq;
using UserService.Application;
using UserService.Controllers;
using UserService.DTOs;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using UserService.Infrastructure.EF;
using Microsoft.EntityFrameworkCore;
using Shared.Domain.Entities;
using System;

namespace UserService.Tests
{
    public class UsersControllerTests
    {
        [Test]
        public async Task Create_Valid_ReturnsCreated()
        {
            var options = new DbContextOptionsBuilder<UserDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var db = new UserDbContext(options);
            var outboxRepo = new UserService.Infrastructure.EF.OutboxRepository(db);
            var svc = new UserAppService(outboxRepo);

            var controller = new UsersController(svc, db);
            var dto = new UserCreateDto { Name = "abc", Email = "x@y.com" };

            var res = await controller.Create(dto);
            Assert.IsInstanceOf<CreatedAtActionResult>(res);
        }
    }
}
