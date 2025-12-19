using System;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.OutboxPublisher.Abstractions;
using Shared.OutboxPublisher.Publishers;
using Shared.OutboxPublisher.Worker;

namespace Shared.OutboxPublisher.DI
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the outbox publisher components.
        /// Note: you must register a concrete IOutboxStore implementation before or after calling this.
        /// Example:
        /// services.AddOutboxPublisher(opts => configuration.GetSection("Outbox").Bind(opts));
        /// services.AddSingleton<IOutboxStore, YourDbOutboxStore>();
        /// </summary>
        public static IServiceCollection AddOutboxPublisher(this IServiceCollection services, Action<OutboxPublisherOptions> configure)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            services.Configure(configure);

            // Register IProducer<Null,string> built from configured options
            services.TryAddSingleton<IProducer<Null, string>>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<OutboxPublisherOptions>>().Value;
                var cfg = new ProducerConfig
                {
                    BootstrapServers = opts.KafkaBootstrapServers,
                    // additional safety defaults could be added here
                };

                var logger = sp.GetService<ILogger<KafkaOutboxPublisher>>();
                try
                {
                    var builder = new ProducerBuilder<Null, string>(cfg)
                        .SetValueSerializer(Serializers.Utf8);

                    return builder.Build();
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to create Kafka producer. Ensure Confluent.Kafka is available and bootstrap servers are correct.");
                    throw;
                }
            });

            services.TryAddSingleton<IOutboxPublisher, KafkaOutboxPublisher>();

            // Register the worker as a hosted service
            services.AddSingleton<OutboxPublisherWorker>();
            services.AddHostedService(sp => sp.GetRequiredService<OutboxPublisherWorker>());

            return services;
        }
    }
}