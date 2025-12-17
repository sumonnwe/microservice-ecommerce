using System;

namespace UserService.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string Email { get; set; } = default!;
        public bool IsActive { get; set; } = true;
        public DateTime? LastSeenUtc { get; set; }
        public DateTime? InactiveSinceUtc { get; set; } // New: when set, indicates we already emitted a users.inactive event at this time.
        // Used to avoid repeatedly emitting the same inactive event every worker loop.
    }
}
