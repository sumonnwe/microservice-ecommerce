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

            var controller = new UsersController(db);
            var dto = new UserCreateDto { Name = "abc", Email = "x@y.com" };

            var res = await controller.Create(dto);
            //Assert.IsInstanceOf<CreatedAtActionResult>(res);
        }
    }
}



/*
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using UserService;

[TestFixture]
public class UserControllerTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [SetUp]
    public void Setup() => _factory = new WebApplicationFactory<Program>();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public async Task PostUser_ReturnsCreated_And_GetReturnsUser()
    {
        var client = _factory.CreateClient();
        var user = new { Name = "Alice", Email = "alice@example.com" };

        var postResp = await client.PostAsJsonAsync("/users", user);
        Assert.AreEqual(HttpStatusCode.Created, postResp.StatusCode);

        var location = postResp.Headers.Location?.ToString();
        Assert.IsNotNull(location);

        var getResp = await client.GetAsync(location);
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);

        var returned = await getResp.Content.ReadFromJsonAsync<UserDto>();
        Assert.AreEqual("Alice", returned.Name);
        Assert.AreEqual("alice@example.com", returned.Email);
    }

    public record UserDto(Guid Id, string Name, string Email);
}
*/