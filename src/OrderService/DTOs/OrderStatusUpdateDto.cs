namespace OrderService.DTOs
{
    public class OrderStatusUpdateDto
    {
        public string NewStatus { get; set; } = default!;
        public string? Reason { get; set; }
    }
}