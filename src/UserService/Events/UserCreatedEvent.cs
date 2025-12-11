using System;

namespace UserService.Events
{
    public class UserCreatedEvent
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string Email { get; set; } = default!;
    }
}
