using System;

namespace Shared.OutboxPublisher
{
    public sealed class OutboxPublisherOptions
    {
        public string KafkaBootstrapServers { get; set; } = "kafka:9092";
        public int PollIntervalMs { get; set; } = 5000;
        public int BatchSize { get; set; } = 50;
        public int LockDurationSeconds { get; set; } = 60;
        public int MaxRetries { get; set; } = 5;
        public string DeadLetterTopic { get; set; } = "dead-letter";
    }
}