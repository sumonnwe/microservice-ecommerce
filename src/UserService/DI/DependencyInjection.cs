using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using UserService.Infrastructure.EF;
using Shared.Domain.Services;
using UserService.Application;
using UserService.Infrastructure.Kafka;
using UserService.Infrastructure.EF;
using UserService.BackgroundServices;
using Serilog;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UserService.DI
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddUserService(this IServiceCollection services, IConfiguration cfg)
        {
            var conn = cfg["CONNECTIONSTRING"] ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=ecommerce";
            services.AddDbContext<UserDbContext>(opt => opt.UseNpgsql(conn));

            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<UserAppService>();
            services.AddSingleton<IKafkaProducer>(_ => new KafkaProducer(cfg["KAFKA_BOOTSTRAP_SERVERS"] ?? "kafka:9092"));

            services.AddHostedService<OutboxDispatcher>();

            // Serilog and OpenTelemetry are configured in Program.cs
            return services;
        }
    }
}
