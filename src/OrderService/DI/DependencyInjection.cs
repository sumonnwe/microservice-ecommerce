using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.EF;
using Shared.Domain.Services;
using OrderService.Application;
using OrderService.Infrastructure.Kafka;

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

            // Background dispatcher is not added here; OutboxDispatcher project will poll this service via HTTP
            return services;
        }
    }
}