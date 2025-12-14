using Microsoft.AspNetCore.Mvc;
using Shared.Domain.Entities;
using System.Text.Json;
using UserService.Domain.Entities;
using UserService.DTOs;
using UserService.Infrastructure.EF;

namespace UserService.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly UserDbContext _db;

        public UsersController(UserDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UserCreateDto dto)
        {
            var user = new User { Id = Guid.NewGuid(), Name = dto.Name, Email = dto.Email };

            var evt = new { Id = user.Id, Name = user.Name, Email = user.Email };
            var outbox = new OutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "users.created",
                AggregateId = user.Id,
                Payload = JsonSerializer.Serialize(evt),
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            // Single DbContext -> single transaction when SaveChangesAsync is called.
            _db.Users.Add(user);
            _db.OutboxEntries.Add(outbox);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            return Ok(user);
        }
    }

    //public record CreateUserDto(string Name, string Email);
}
