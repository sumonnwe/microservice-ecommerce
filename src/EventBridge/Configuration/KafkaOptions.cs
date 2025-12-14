namespace EventBridge.Configuration
{
    public class KafkaOptions
    {
        public string BootstrapServers { get; set; } = "kafka:9092";
        public string[] Topics { get; set; } = new[] { "users.created", "orders.created", "dead-letter" };
        public string GroupId { get; set; } = "eventBridge-group";
    }
}