using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application;
using OrderService.BackgroundServices;
using OrderService.Handlers;
using OrderService.Infrastructure.EF;
using OrderService.Infrastructure.Kafka;
using Shared.Domain.Services;

namespace OrderService.DI
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddOrderService(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<OrderDbContext>(opt => opt.UseInMemoryDatabase("orderdb"));
            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<OrderAppService>();

            // Read bootstrap from IConfiguration and pass the string into KafkaProducer
            var bootstrap = configuration["KAFKA_BOOTSTRAP_SERVERS"] ?? "kafka:9092";
            services.AddSingleton<Shared.Domain.Services.IKafkaProducer>(_ => new KafkaProducer(bootstrap));

            // Register handler and Kafka consumer hosted service
            services.AddScoped<UserStatusChangedHandler>();
            services.AddHostedService<UserStatusConsumerService>();  
            // Background dispatcher is not added here; OutboxDispatcher project will poll this service via HTTP
            return services;
        }
    }
}