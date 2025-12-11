using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.EF;
using Shared.Domain.Services;
using OrderService.Infrastructure.Kafka;
using OrderService.BackgroundServices;
using Serilog;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OrderService.DI
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddOrderService(this IServiceCollection services, IConfiguration cfg)
        {
            var conn = cfg["CONNECTIONSTRING"] ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=ecommerce";
            services.AddDbContext<OrderDbContext>(opt => opt.UseNpgsql(conn));

            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddSingleton<IKafkaProducer>(_ => new KafkaProducer(cfg["KAFKA_BOOTSTRAP_SERVERS"] ?? "kafka:9092"));
            services.AddHostedService<OutboxDispatcher>();
            return services;
        }
    }
}
