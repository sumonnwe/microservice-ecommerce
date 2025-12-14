using NUnit.Framework;
using Moq;
using OrderService.Application;
using OrderService.Controllers;
using OrderService.DTOs;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using OrderService.Infrastructure.EF;
using Microsoft.EntityFrameworkCore;
using Shared.Domain.Entities;
using System;

namespace OrderService.Tests
{
    public class OrderControllerTests
    {
        [Test]
        public async Task Create_Valid_ReturnsCreated()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var db = new OrderDbContext(options);
            var outboxRepo = new OrderService.Infrastructure.EF.OutboxRepository(db);
            var svc = new OrderAppService(outboxRepo);

            var controller = new OrdersController(db);
            var dto = new OrderCreateDto { UserId = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), Product = "Men Cloth", Quantity = 3, Price = (Decimal)49.95 }; 
            var res = await controller.Create(dto);
            Assert.IsInstanceOf<CreatedAtActionResult>(res);
        }
    }
}
