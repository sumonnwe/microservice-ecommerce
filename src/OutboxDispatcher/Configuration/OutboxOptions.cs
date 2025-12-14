namespace OutboxDispatcher.Configuration
{
    public class OutboxOptions
    {
        public string? KafkaBootstrapServers { get; set; } = "kafka:9092";
        public string[]? ServiceUrls { get; set; } = new[] { "http://userservice:8080", "http://orderservice:8080" };
        public int PollIntervalMs { get; set; } = 5000;
        public int MaxRetries { get; set; } = 5;
    }
}