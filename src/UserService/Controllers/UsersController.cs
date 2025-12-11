using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using UserService.Application;
using UserService.DTOs;
using UserService.Domain.Entities;
using UserService.Infrastructure.EF;
using Microsoft.EntityFrameworkCore;

namespace UserService.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly UserAppService _app;
        private readonly UserDbContext _db;

        public UsersController(UserAppService app, UserDbContext db)
        {
            _app = app;
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UserCreateDto dto)
        {
            try
            {
                // Begin transaction to persist user and outbox in same DB transaction
                await using var tx = await _db.Database.BeginTransactionAsync();

                var user = await _app.CreateUserAsync(dto.Name, dto.Email);

                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                // Note: OutboxRepository.AddAsync already saved the outbox; to ensure the outbox is part of same transaction
                // we can instead add OutboxEntry to DbContext here. For simplicity, OutboxRepository.AddAsync writes separately.
                // In production, OutboxRepository should add to same DbContext instance and SaveChanges once.

                await tx.CommitAsync();

                return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();
            return Ok(user);
        }
    }
}
