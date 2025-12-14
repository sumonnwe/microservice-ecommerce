using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Domain.Entities;
using OrderService.Infrastructure.EF;

namespace OrderService.Controllers
{
    [ApiController]
    [Route("api/outbox")]
    public class OutboxController : ControllerBase
    {
        private readonly OrderDbContext _db;
        private readonly ILogger<OutboxController> _logger;

        public OutboxController(OrderDbContext db, ILogger<OutboxController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // Dispatcher calls this to get unsent entries
        [HttpGet("unsent")]
        public async Task<ActionResult<List<OutboxEntry>>> GetUnsent([FromQuery] int max = 50, CancellationToken cancellationToken = default)
        {
            const int MaxAllowed = 500;
            if (max <= 0) max = 1;
            if (max > MaxAllowed) max = MaxAllowed;

            try
            {
                var list = await _db.OutboxEntries
                    .Where(e => e.SentAt == null && e.RetryCount < 5)
                    .OrderBy(e => e.CreatedAt)
                    .Take(max)
                    .ToListAsync(cancellationToken);

                return Ok(list);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("GetUnsent cancelled. TraceId={TraceId}", HttpContext?.TraceIdentifier);
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching unsent outbox entries. TraceId={TraceId}", HttpContext?.TraceIdentifier);
                return Problem(detail: "Failed to fetch unsent outbox entries.",
                               statusCode: StatusCodes.Status500InternalServerError,
                               instance: HttpContext?.TraceIdentifier);
            }
        }

        [HttpPost("mark-sent/{id:guid}")]
        public async Task<IActionResult> MarkSent(Guid id, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty)
                return BadRequest(new ProblemDetails { Title = "Invalid id", Detail = "Id must be a non-empty GUID." });

            try
            {
                var entry = await _db.OutboxEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
                if (entry == null)
                {
                    _logger.LogWarning("MarkSent: outbox entry not found. Id={Id}", id);
                    return NotFound();
                }

                entry.SentAt = DateTime.UtcNow;

                using var saveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                saveCts.CancelAfter(TimeSpan.FromSeconds(15));

                await _db.SaveChangesAsync(saveCts.Token);

                _logger.LogInformation("Marked outbox entry sent. Id={Id}", id);
                return NoContent();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("MarkSent cancelled for Id={Id}. TraceId={TraceId}", id, HttpContext?.TraceIdentifier);
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error marking outbox entry sent. Id={Id} TraceId={TraceId}", id, HttpContext?.TraceIdentifier);
                return Problem(detail: "Database error while marking outbox entry sent.",
                               statusCode: StatusCodes.Status500InternalServerError,
                               instance: HttpContext?.TraceIdentifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error marking outbox entry sent. Id={Id} TraceId={TraceId}", id, HttpContext?.TraceIdentifier);
                return Problem(detail: "Unexpected error while marking outbox entry sent.",
                               statusCode: StatusCodes.Status500InternalServerError,
                               instance: HttpContext?.TraceIdentifier);
            }
        }

        [HttpPost("increment-retry/{id:guid}")]
        public async Task<IActionResult> IncrementRetry(Guid id, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty)
                return BadRequest(new ProblemDetails { Title = "Invalid id", Detail = "Id must be a non-empty GUID." });

            try
            {
                var entry = await _db.OutboxEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
                if (entry == null)
                {
                    _logger.LogWarning("IncrementRetry: outbox entry not found. Id={Id}", id);
                    return NotFound();
                }

                entry.RetryCount++;

                using var saveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                saveCts.CancelAfter(TimeSpan.FromSeconds(15));

                await _db.SaveChangesAsync(saveCts.Token);

                _logger.LogInformation("Incremented retry for outbox entry. Id={Id} RetryCount={RetryCount}", id, entry.RetryCount);
                return NoContent();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("IncrementRetry cancelled for Id={Id}. TraceId={TraceId}", id, HttpContext?.TraceIdentifier);
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error incrementing retry. Id={Id} TraceId={TraceId}", id, HttpContext?.TraceIdentifier);
                return Problem(detail: "Database error while updating retry count.",
                               statusCode: StatusCodes.Status500InternalServerError,
                               instance: HttpContext?.TraceIdentifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error incrementing retry. Id={Id} TraceId={TraceId}", id, HttpContext?.TraceIdentifier);
                return Problem(detail: "Unexpected error while updating retry count.",
                               statusCode: StatusCodes.Status500InternalServerError,
                               instance: HttpContext?.TraceIdentifier);
            }
        }
    }
}