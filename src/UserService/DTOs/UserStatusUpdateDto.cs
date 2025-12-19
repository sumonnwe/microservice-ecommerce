namespace UserService.DTOs
{
    public class UserStatusUpdateDto
    {
        // Example: "Inactive"
        public string NewStatus { get; set; } = default!;

        // Optional reason, e.g. "manual_admin_action"
        public string? Reason { get; set; }
    }
}
