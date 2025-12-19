namespace OrderService.Configuration
{
    public class KafkaOptions
    {
        public required string BootstrapServers { get; set; }
        public required string[] Topics { get; set; }
        public required string GroupId { get; set; }
    }
}