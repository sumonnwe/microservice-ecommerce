using System;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Shared.Domain.Services;

namespace UserService.Infrastructure.Kafka
{
    public class KafkaProducer : IKafkaProducer, IDisposable
    {
        private readonly IProducer<Null, string> _producer;

        // Reads configuration from IConfiguration (appsettings.json / env vars)
        public KafkaProducer(IConfiguration configuration)
        {
            var bootstrap = configuration["KAFKA_BOOTSTRAP_SERVERS"] ?? "kafka:9092";
            var cfg = new ProducerConfig { BootstrapServers = bootstrap, Acks = Acks.All };
            _producer = new ProducerBuilder<Null, string>(cfg).Build();
        }

        public async Task ProduceAsync(string topic, string payload)
        {
            var msg = new Message<Null, string> { Value = payload };
            var res = await _producer.ProduceAsync(topic, msg);
            if (res.Status != PersistenceStatus.Persisted)
            {
                throw new Exception($"Failed to persist message to {topic}");
            }
        }

        public void Dispose() => _producer?.Dispose();
    }
}