using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR;
using EventBridge.Hubs;

namespace EventBridge.BackgroundServices
{
    public class KafkaConsumerService : BackgroundService
    {
        private readonly ILogger<KafkaConsumerService> _logger;
        private readonly IHubContext<EventHub> _hub;
        private readonly string _bootstrap;

        public KafkaConsumerService(ILogger<KafkaConsumerService> logger, IHubContext<EventHub> hub, IConfiguration cfg)
        {
            _logger = logger;
            _hub = hub;
            _bootstrap = cfg["KAFKA_BOOTSTRAP_SERVERS"] ?? "kafka:9092";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var conf = new ConsumerConfig
            {
                BootstrapServers = _bootstrap,
                GroupId = "eventbridge-group",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(conf).Build();
            consumer.Subscribe(new[] { "users.created", "orders.created", "dead-letter" });

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cr = consumer.Consume(stoppingToken);
                    await _hub.Clients.All.SendAsync("ReceiveEvent", cr.Topic, cr.Message.Value, cancellationToken: stoppingToken);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error consuming Kafka");
                }
            }
        }
    }
}
