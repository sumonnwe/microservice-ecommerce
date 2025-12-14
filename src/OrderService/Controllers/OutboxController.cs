using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Shared.Domain.Entities;
using OrderService.Infrastructure.EF;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;

namespace OrderService.Controllers
{
    [ApiController]
    [Route("api/outbox")]
    public class OutboxController : ControllerBase
    {
        private readonly OrderDbContext _db;
        public OutboxController(OrderDbContext db) => _db = db;

        // Dispatcher calls this to get unsent entries
        [HttpGet("unsent")]
        public async Task<ActionResult<List<OutboxEntry>>> GetUnsent()
        {
            var list = await _db.OutboxEntries.Where(e => e.SentAt == null && e.RetryCount < 5).OrderBy(e => e.CreatedAt).ToListAsync();
            return Ok(list);
        }

        [HttpPost("mark-sent/{id:guid}")]
        public async Task<IActionResult> MarkSent(Guid id)
        {
            var e = await _db.OutboxEntries.FindAsync(id);
            if (e == null) return NotFound();
            e.SentAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("increment-retry/{id:guid}")]
        public async Task<IActionResult> IncrementRetry(Guid id)
        {
            var e = await _db.OutboxEntries.FindAsync(id);
            if (e == null) return NotFound();
            e.RetryCount++;
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
