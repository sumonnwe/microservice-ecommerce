using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.EF;
using Shared.Domain.Services;
using OrderService.Application;
using OrderService.Infrastructure.Kafka;

namespace OrderService.DI
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddOrderService(this IServiceCollection services, string kafkaBootstrap)
        {
            services.AddDbContext<OrderDbContext>(opt => opt.UseInMemoryDatabase("orderdb"));
            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<OrderAppService>();
            services.AddSingleton<Shared.Domain.Services.IKafkaProducer>(_ => new KafkaProducer(kafkaBootstrap));
            // Background dispatcher is not added here; OutboxDispatcher project will poll this service via HTTP
            return services;
        }
    }
}
