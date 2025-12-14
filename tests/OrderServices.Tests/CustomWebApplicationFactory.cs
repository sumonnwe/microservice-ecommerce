using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using OrderService.Infrastructure.EF;
using System;
using System.Linq;

namespace OrderService.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Use a unique in-memory database name per factory instance to avoid cross-test interference.
    private readonly string _inMemoryDbName = $"OrderTestDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContextOptions<OrderDbContext> registration if present
            var dbOptionsDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<OrderDbContext>));
            if (dbOptionsDescriptor != null)
                services.Remove(dbOptionsDescriptor);

            // Remove any existing OrderDbContext registration to replace with test one
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(OrderDbContext));
            if (dbContextDescriptor != null)
                services.Remove(dbContextDescriptor);

            // Register an in-memory OrderDbContext for tests
            services.AddDbContext<OrderDbContext>(options =>
                options.UseInMemoryDatabase(_inMemoryDbName));

            // Ensure the database schema is created before tests run
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            db.Database.EnsureCreated();
        });
    }
}