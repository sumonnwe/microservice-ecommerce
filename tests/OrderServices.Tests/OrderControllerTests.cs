using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using OrderService.Controllers;
using OrderService.Domain.Entities;
using OrderService.DTOs;
using OrderService.Infrastructure.EF;
using Shared.Domain.Entities;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OrderService.Tests
{
    [TestFixture]
    public class OrderControllerTests
    {
        // Simple HttpMessageHandler stub for producing controlled responses
        private class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;
            public StubHttpMessageHandler(HttpResponseMessage response) => _response = response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(_response);
        }

        private static HttpClient CreateHttpClient(HttpResponseMessage response)
        {
            var handler = new StubHttpMessageHandler(response);
            // base address used by controller when constructing request URL fallback
            var client = new HttpClient(handler) { BaseAddress = new Uri("http://userservice:8080") };
            return client;
        }

        [Test]
        public async Task Create_Valid_ReturnsCreated_And_PersistsOutboxEntry()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new OrderDbContext(options);
            var logger = new Mock<ILogger<OrdersController>>();

            // Arrange: user exists -> return 200 OK
            var userResp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"00000000-0000-0000-0000-000000000000\"}") };
            var httpClient = CreateHttpClient(userResp);
            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var controller = new OrdersController(db, logger.Object, httpFactory.Object);

            var dto = new OrderCreateDto
            {
                UserId = Guid.NewGuid(),
                Product = "Men Cloth",
                Quantity = 3,
                Price = 49.95m
            };

            var res = await controller.Create(dto, CancellationToken.None);

            Assert.That(res, Is.InstanceOf<CreatedAtActionResult>());
            var created = (CreatedAtActionResult)res;
            Assert.That(created.ActionName, Is.EqualTo(nameof(OrdersController.Get)));

            var returned = created.Value as Order;
            Assert.That(returned, Is.Not.Null);
            Assert.That(returned!.Product, Is.EqualTo("Men Cloth"));
            Assert.That(returned.Quantity, Is.EqualTo(3));
            Assert.That(returned.Price, Is.EqualTo(49.95m));

            var persisted = await db.Orders.FindAsync(returned.Id);
            Assert.That(persisted, Is.Not.Null);

            var outbox = db.OutboxEntries.SingleOrDefault(e => e.AggregateId == returned.Id);
            Assert.That(outbox, Is.Not.Null);
            Assert.That(outbox!.EventType, Is.EqualTo("orders.created"));
            StringAssert.Contains("\"Product\":\"Men Cloth\"", outbox.Payload);
        }

        [Test]
        public async Task Create_UserNotFound_ReturnsBadRequest()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new OrderDbContext(options);
            var logger = new Mock<ILogger<OrdersController>>();

            // Arrange: user service returns 404 -> controller should return BadRequest (invalid user)
            var userResp = new HttpResponseMessage(HttpStatusCode.NotFound);
            var httpClient = CreateHttpClient(userResp);
            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var controller = new OrdersController(db, logger.Object, httpFactory.Object);

            var dto = new OrderCreateDto
            {
                UserId = Guid.NewGuid(),
                Product = "Item",
                Quantity = 1,
                Price = 9.99m
            };

            var res = await controller.Create(dto, CancellationToken.None);

            Assert.That(res, Is.InstanceOf<ObjectResult>());
            var obj = (ObjectResult)res;
            Assert.That(obj.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        }

        [Test]
        public async Task Create_UserServiceError_Returns503()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new OrderDbContext(options);
            var logger = new Mock<ILogger<OrdersController>>();

            // Arrange: user service returns 500 -> treated as transient -> controller returns 503
            var userResp = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            var httpClient = CreateHttpClient(userResp);
            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var controller = new OrdersController(db, logger.Object, httpFactory.Object);

            var dto = new OrderCreateDto
            {
                UserId = Guid.NewGuid(),
                Product = "Item",
                Quantity = 1,
                Price = 9.99m
            };

            var res = await controller.Create(dto, CancellationToken.None);

            Assert.That(res, Is.InstanceOf<ObjectResult>());
            var obj = (ObjectResult)res;
            Assert.That(obj.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        }

        [Test]
        public async Task Create_InvalidPayload_ReturnsBadRequest_WhenQuantityOrPriceInvalid()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new OrderDbContext(options);
            var logger = new Mock<ILogger<OrdersController>>();
            var controller = new OrdersController(db, logger.Object);

            var dto1 = new OrderCreateDto
            {
                UserId = Guid.NewGuid(),
                Product = "Item",
                Quantity = 0,
                Price = 10m
            };

            var res1 = await controller.Create(dto1, CancellationToken.None);
            Assert.That(res1, Is.InstanceOf<ObjectResult>());
            var obj1 = (ObjectResult)res1;
            Assert.That(obj1.StatusCode, Is.EqualTo(400));

            var dto2 = new OrderCreateDto
            {
                UserId = Guid.NewGuid(),
                Product = "Item",
                Quantity = 1,
                Price = 0m
            };

            var res2 = await controller.Create(dto2, CancellationToken.None);
            Assert.That(res2, Is.InstanceOf<ObjectResult>());
            var obj2 = (ObjectResult)res2;
            Assert.That(obj2.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public async Task Create_NullDto_ReturnsBadRequest()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new OrderDbContext(options);
            var logger = new Mock<ILogger<OrdersController>>();
            var controller = new OrdersController(db, logger.Object);

            var res = await controller.Create(null!, CancellationToken.None);

            Assert.That(res, Is.InstanceOf<ObjectResult>());
            var obj = (ObjectResult)res;
            Assert.That(obj.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public async Task Create_RequestCancelledBeforeSave_Returns499()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new OrderDbContext(options);
            var logger = new Mock<ILogger<OrdersController>>();
            var controller = new OrdersController(db, logger.Object);

            var dto = new OrderCreateDto
            {
                UserId = Guid.NewGuid(),
                Product = "Item",
                Quantity = 1,
                Price = 9.99m
            };

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var res = await controller.Create(dto, cts.Token);

            Assert.That(res, Is.InstanceOf<ObjectResult>());
            var obj = (ObjectResult)res;
            Assert.That(obj.StatusCode, Is.EqualTo(499).Or.EqualTo(StatusCodes.Status499ClientClosedRequest));
        }
    }
}