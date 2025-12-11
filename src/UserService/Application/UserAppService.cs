using System;
using System.Text.Json;
using System.Threading.Tasks;
using Shared.Domain.Entities;
using Shared.Domain.Services;
using UserService.Domain.Entities;
using UserService.Events;

namespace UserService.Application
{
    // Domain logic: validate and create user; create OutboxEntry in same DB transaction
    public class UserAppService
    {
        private readonly IOutboxRepository _outboxRepository;

        public UserAppService(IOutboxRepository outboxRepository)
        {
            _outboxRepository = outboxRepository;
        }

        public async Task<User> CreateUserAsync(string name, string email)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required", nameof(name));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email required", nameof(email));

            var user = new User { Id = Guid.NewGuid(), Name = name, Email = email };

            var evt = new UserCreatedEvent { Id = user.Id, Name = user.Name, Email = user.Email };
            var outbox = new OutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "users.created",
                AggregateId = user.Id,
                Payload = JsonSerializer.Serialize(evt),
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            // The persistence + outbox write occurs in Infrastructure via a single DbContext SaveChanges in controller
            await _outboxRepository.AddAsync(outbox);

            return user;
        }
    }
}
