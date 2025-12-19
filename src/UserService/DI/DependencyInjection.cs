using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UserService.Application;
using UserService.Infrastructure.EF;
using Shared.Domain.Services;
using Shared.OutboxPublisher.DI;
using Shared.OutboxPublisher.Abstractions;
 

namespace UserService.DI
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddUserService(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<UserDbContext>(opt => opt.UseInMemoryDatabase("userdb"));
            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<UserAppService>();
            //services.AddSingleton<Shared.Domain.Services.IKafkaProducer>(_ => new KafkaProducer(configuration));

            // Register Shared Outbox Publisher and the EF-backed store adapter for this service
            services.AddOutboxPublisher(opts => configuration.GetSection("Outbox").Bind(opts));
            services.AddScoped<IOutboxStore, OutboxStore>();

            // Background dispatcher is not added here; OutboxDispatcher project will poll this service via HTTP
            return services;
        }
    }
}
