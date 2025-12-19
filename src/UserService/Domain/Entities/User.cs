using System;
using Shared.Domain;

namespace UserService.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string Email { get; set; } = default!;

        // New: Status persisted as string (configured in DbContext).
        public UserStatus Status { get; set; } = UserStatus.Active;
    }
}
