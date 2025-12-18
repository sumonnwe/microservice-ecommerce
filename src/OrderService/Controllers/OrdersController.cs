using Humanizer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderService.Domain.Entities;
using OrderService.DTOs;
using OrderService.Infrastructure.EF;
using Shared.Domain.Entities;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OrderService.Controllers
{
    /// <summary>
    /// Manage orders.
    /// </summary>
    [ApiController]
    [Route("api/orders")]
    [Produces("application/json")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderDbContext _db;
        private readonly ILogger<OrdersController> _logger;
        private readonly IHttpClientFactory? _httpClientFactory;

        // IHttpClientFactory optional to avoid breaking existing tests that construct controller directly.
        public OrdersController(OrderDbContext db, ILogger<OrdersController> logger, IHttpClientFactory? httpClientFactory = null)
        {
            _db = db;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Create a new order and enqueue an "orders.created" outbox event.
        /// </summary>
        /// <param name="dto">Order payload.</param>
        /// <param name="cancellationToken">Request cancellation token.</param>
        /// <returns>201 Created with created order or ProblemDetails on error.</returns>
        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(Order), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create([FromBody] OrderCreateDto dto, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Create order request started.");

            if (dto is null)
            {
                _logger.LogWarning("Create order request contained a null body.");
                return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "Request body cannot be empty." });
            }

            // Field-level validation
            if (dto.UserId == Guid.Empty)
                ModelState.AddModelError(nameof(dto.UserId), "UserId is required.");

            if (string.IsNullOrWhiteSpace(dto.Product))
                ModelState.AddModelError(nameof(dto.Product), "Product is required.");

            if (dto.Quantity <= 0)
                ModelState.AddModelError(nameof(dto.Quantity), "Quantity must be > 0.");

            if (dto.Price <= 0m)
                ModelState.AddModelError(nameof(dto.Price), "Price must be > 0.");

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Validation failed for Create order request: {@ModelState}", ModelState);
                return BadRequest(new ValidationProblemDetails(ModelState)
                {
                    Title = "Validation error",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = HttpContext?.TraceIdentifier
                });
            }

            // Validate user exists via UserService if an IHttpClientFactory was provided
            if (_httpClientFactory != null)
            {
                _logger.LogInformation("========================httpClientFactory available - validating user existence for UserId={UserId}", dto.UserId);
                try
                {
                    var userExists = await ValidateUserExistsAsync(dto.UserId, cancellationToken);
                    if (!userExists)
                    {
                        _logger.LogWarning("User validation failed: user not found. UserId={UserId}", dto.UserId);
                        return BadRequest(new ProblemDetails { Title = "Invalid user", Detail = "User does not exist." });
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Failed to validate user with UserService. UserId={UserId}", dto.UserId);
                    return Problem(detail: "Unable to validate user at this time.", statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("User validation cancelled for UserId={UserId}", dto.UserId);
                    return new ObjectResult(new ProblemDetails { Title = "Request cancelled", Detail = "User validation was cancelled." })
                    { StatusCode = StatusCodes.Status499ClientClosedRequest };
                }
            }
            else
            {
                _logger.LogDebug("IHttpClientFactory not available - skipping remote user validation for UserId={UserId}", dto.UserId);
            }

            // Normalize
            var product = dto.Product.Trim();

            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = dto.UserId,
                Product = product,
                Quantity = dto.Quantity,
                Price = dto.Price,
                Status = OrderStatus.Pending,

            };

            var evt = new
            {
                Id = order.Id,
                UserId = order.UserId,
                Product = order.Product,
                Quantity = order.Quantity,
                Price = order.Price,
                Status = order.Status,
            };

            var outbox = new OutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "orders.created",
                AggregateId = order.Id,
                Payload = JsonSerializer.Serialize(evt),
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                // Use a linked token with a timeout so client disconnects don't immediately cancel DB writes.
                using var saveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                saveCts.CancelAfter(TimeSpan.FromSeconds(15));
                var saveToken = saveCts.Token;

                // Some providers (InMemory) do not support BeginTransaction; detect and use accordingly.
                var provider = _db.Database.ProviderName ?? string.Empty;
                if (provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
                {
                    _db.Orders.Add(order);
                    _db.OutboxEntries.Add(outbox);
                    await _db.SaveChangesAsync(saveToken);
                }
                else
                {
                    await using var tx = await _db.Database.BeginTransactionAsync(saveToken);
                    try
                    {
                        _db.Orders.Add(order);
                        _db.OutboxEntries.Add(outbox);
                        await _db.SaveChangesAsync(saveToken);
                        await tx.CommitAsync(saveToken);
                    }
                    catch
                    {
                        await tx.RollbackAsync(CancellationToken.None);
                        throw;
                    }
                }

                _logger.LogInformation("Order created successfully with id={OrderId} userId={UserId} product={Product}", order.Id, order.UserId, order.Product);
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database update error while creating order for user {UserId}. TraceId={TraceId}", dto.UserId, HttpContext?.TraceIdentifier);
                var baseEx = dbEx.GetBaseException();
                _logger.LogDebug("DbUpdateException base exception: {BaseException}", baseEx?.ToString());
                return Problem(detail: "An error occurred while creating the order.", statusCode: StatusCodes.Status500InternalServerError, instance: HttpContext?.TraceIdentifier);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Create order request cancelled for user {UserId}. TraceId={TraceId}", dto.UserId, HttpContext?.TraceIdentifier);
                return new ObjectResult(new ProblemDetails { Title = "Request cancelled", Detail = "The request was cancelled before completion." })
                { StatusCode = StatusCodes.Status499ClientClosedRequest };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating order for user {UserId}. TraceId={TraceId}", dto.UserId, HttpContext?.TraceIdentifier);
                return Problem(detail: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError, instance: HttpContext?.TraceIdentifier);
            }

            return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
        }

        /// <summary>
        /// Get order by id.
        /// </summary>
        /// <param name="id">Order id (GUID).</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id)
        {
            try
            {
                var order = await _db.Orders.FindAsync(id);
                if (order == null) return NotFound();
                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching order {OrderId}", id);
                return Problem(detail: "Failed to fetch order.", statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        // Attempts to call UserService GET /api/users/{id}. Returns true if user exists (200), false if 404.
        private async Task<bool> ValidateUserExistsAsync(Guid userId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Validate User ID at https://userservice:8080/api/users/{userId}", userId);

            if (_httpClientFactory == null) return true; // defensive

            var client = _httpClientFactory.CreateClient();

            // Determine base address:
            // - prefer client's BaseAddress if configured
            // - fallback to environment variable USER_SERVICE_BASE_URL
            // - final fallback to docker-style host name "http://userservice:8080"
            var baseUrl = client.BaseAddress?.ToString().TrimEnd('/') ??
                          Environment.GetEnvironmentVariable("USER_SERVICE_BASE_URL") ??
                          "http://userservice:8080";

            var requestUrl = $"{baseUrl}/api/users/{userId}";

            using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);

            var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (resp.StatusCode == HttpStatusCode.OK) return true;
            if (resp.StatusCode == HttpStatusCode.NotFound) return false;

            // treat other statuses as transient errors
            _logger.LogWarning("Unexpected response from UserService while validating user. StatusCode={Status} UserId={UserId}", resp.StatusCode, userId);
            throw new HttpRequestException($"Unexpected status code {resp.StatusCode} from UserService");
        }
    }
}