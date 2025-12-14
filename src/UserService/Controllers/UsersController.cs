using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Domain.Entities;
using System.Text.Json;
using UserService.Domain.Entities;
using UserService.DTOs;
using UserService.Infrastructure.EF;

namespace UserService.Controllers
{
    /// <summary>
    /// Manage users.
    /// </summary>
    /// <remarks>
    /// Endpoints:
    /// - POST /api/users : create a user and enqueue a "users.created" outbox event.
    /// - GET  /api/users/{id} : retrieve a user by id.
    /// </remarks>
    [ApiController]
    [Route("api/users")]
    [Produces("application/json")]
    public class UsersController : ControllerBase
    {
        private readonly UserDbContext _db;
        private readonly ILogger<UsersController> _logger; 

        public UsersController(UserDbContext db, ILogger<UsersController> logger)
        {
            _db = db;
            _logger = logger; 
        }

        /// <summary>
        /// Create a new user.
        /// </summary>
        /// <param name="dto">User creation payload.</param>
        /// <param name="cancellationToken">Request cancellation token (bound to RequestAborted).</param>
        /// <returns>201 Created with the created user, or a ProblemDetails on error.</returns>
        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(User), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create([FromBody] UserCreateDto dto, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Create user request started.");

            // Basic null-check
            if (dto is null)
            {
                _logger.LogWarning("Create request contained a null body.");
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid request",
                    Detail = "Request body cannot be empty."
                });
            }

            _logger.LogDebug("Validating request for email={Email}", dto.Email);

            // Field-level validation
            if (string.IsNullOrWhiteSpace(dto.Name))
                ModelState.AddModelError(nameof(dto.Name), "Name is required.");

            if (string.IsNullOrWhiteSpace(dto.Email))
                ModelState.AddModelError(nameof(dto.Email), "Email is required.");
            else
            {
                try
                {
                    // Lightweight email syntax validation
                    var _ = new System.Net.Mail.MailAddress(dto.Email);
                }
                catch
                {
                    ModelState.AddModelError(nameof(dto.Email), "Email is not in a valid format.");
                }
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Validation failed for Create user request: {@ModelState}", ModelState);
                return BadRequest(new ValidationProblemDetails(ModelState)
                {
                    Title = "Validation error",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = HttpContext?.TraceIdentifier
                });
            }

            // Normalize inputs
            var email = dto.Email.Trim();
            var name = dto.Name.Trim();

            _logger.LogDebug("Checking for existing user with email={Email}", email);

            // Check for existing user with same email and return 409 Conflict if found
            var exists = await _db.Users.AnyAsync(u => u.Email == email, cancellationToken);
            if (exists)
            {
                _logger.LogWarning("Attempt to create duplicate user with email={Email}", email);
                return Conflict(new ProblemDetails
                {
                    Title = "Conflict",
                    Detail = "A user with the provided email already exists."
                });
            }

            var user = new User { Id = Guid.NewGuid(), Name = name, Email = email };

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

            try
            {
                _logger.LogDebug("Beginning transaction to insert user and outbox entry for email={Email}", email);

                using var saveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                saveCts.CancelAfter(TimeSpan.FromSeconds(15)); // tune as appropriate for your environment

                _db.Users.Add(user);
                _db.OutboxEntries.Add(outbox);

                await _db.SaveChangesAsync(saveCts.Token);

                _logger.LogInformation("User created successfully with id={UserId} email={Email}", user.Id, email);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
            {
                // Log full exception stack and inner exceptions for diagnosis
                _logger.LogError(dbEx, "Database update error while creating user with email {Email}. TraceId={TraceId}. Exception: {ExceptionString}",
                    email, HttpContext?.TraceIdentifier, dbEx.ToString());

                var baseEx = dbEx.GetBaseException();
                if (baseEx != null)
                    _logger.LogDebug("DbUpdateException base exception: {BaseException}", baseEx.ToString());

                // Best-effort detection of unique constraint violations (provider-specific)
                var inner = dbEx.InnerException?.Message ?? baseEx?.Message;
                if (!string.IsNullOrEmpty(inner) && inner.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _logger.LogWarning("Unique constraint violation detected while creating user email={Email}", email);
                    return Conflict(new ProblemDetails
                    {
                        Title = "Conflict",
                        Detail = "A user with the provided email already exists.",
                        Instance = HttpContext?.TraceIdentifier
                    });
                }

                // Return Problem with trace id so client can report it
                return Problem(detail: "An error occurred while creating the user. Check server logs with the provided trace id.",
                               statusCode: StatusCodes.Status500InternalServerError,
                               instance: HttpContext?.TraceIdentifier);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Create user request cancelled by client for email={Email} TraceId={TraceId}", email, HttpContext?.TraceIdentifier);
                // Respect cancellation requests
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception ex)
            {
                // Log full exception text to ensure inner exceptions / stack trace are captured
                _logger.LogError(ex, "Unexpected error while creating user with email {Email}. TraceId={TraceId}. Exception: {ExceptionString}",
                    email, HttpContext?.TraceIdentifier, ex.ToString());

                return Problem(detail: "An unexpected error occurred. Check server logs with the provided trace id.",
                               statusCode: StatusCodes.Status500InternalServerError,
                               instance: HttpContext?.TraceIdentifier);
            }

            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }

        /// <summary>
        /// Get a user by id.
        /// </summary>
        /// <param name="id">User id (GUID).</param>
        /// <returns>200 with user or 404 if not found.</returns>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var user = await _db.Users.FindAsync(id);
                if (user == null)
                {
                    _logger.LogInformation("GetById: user not found. Id={UserId}", id);
                    return NotFound();
                }

                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user with Id={UserId}", id);
                return Problem(detail: "Failed to fetch user.", statusCode: StatusCodes.Status500InternalServerError, instance: HttpContext?.TraceIdentifier);
            }
        }
    }
}
