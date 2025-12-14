using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using UserService.Infrastructure.EF;
using Shared.Domain.Services;
using UserService.Application;
using UserService.Infrastructure.Kafka;

namespace UserService.DI
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddUserService(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<UserDbContext>(opt => opt.UseInMemoryDatabase("userdb"));
            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<UserAppService>();
            services.AddSingleton<Shared.Domain.Services.IKafkaProducer>(_ => new KafkaProducer(configuration));
            // Background dispatcher is not added here; OutboxDispatcher project will poll this service via HTTP
            return services;
        }
    }
}
