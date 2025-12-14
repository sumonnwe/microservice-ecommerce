using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using OrderService.Infrastructure.EF;

namespace OrderService.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.Single(
                d => d.ServiceType == typeof(DbContextOptions<OrderDbContext>));

            services.Remove(dbDescriptor);

            services.AddDbContext<OrderDbContext>(opt =>
                opt.UseInMemoryDatabase("OrderTestDb"));
        });
    }
}
