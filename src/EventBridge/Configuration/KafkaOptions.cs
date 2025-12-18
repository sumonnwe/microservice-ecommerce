namespace EventBridge.Configuration
{
    public class KafkaOptions
    {
        public string BootstrapServers { get; set; } = "kafka:9092";
        public string[] Topics { get; set; } = new[] { "users.created", "orders.created", "dead-letter", "users.status-changed", "orders.cancelled" };
        public string GroupId { get; set; } = "eventBridge-group";
    }
}