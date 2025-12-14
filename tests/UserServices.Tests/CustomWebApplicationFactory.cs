using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using System;
using UserService.Infrastructure;
using UserService.Infrastructure.EF;

namespace UserServices.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use a test environment and replace the real DB with an in-memory one
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Remove real DB
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<UserDbContext>));

            if (descriptor != null)
                services.Remove(descriptor);

            // Add InMemory DB
            services.AddDbContext<UserDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestUserDb");
            });
        });
    }
}
