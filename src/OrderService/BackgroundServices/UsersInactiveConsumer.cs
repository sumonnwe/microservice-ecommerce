using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Shared.Domain.Entities;
using OrderService.Infrastructure.EF;
using OrderService.Domain.Entities;

namespace OrderService.BackgroundServices
{
    /// <summary>
    /// Consumes "users.inactive" events from Kafka and cancels matching orders for the inactive user.
    /// For each cancelled order an OutboxEntry with EventType="orders.cancelled" is created.
    /// Logic is idempotent: only orders in PendingPayment or Ready are affected.
    /// </summary>
    public class UsersInactiveConsumer : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UsersInactiveConsumer> _logger;
        private readonly string _bootstrap;
        private readonly string[] _topics = new[] { "users.inactive" };
        private readonly string _groupId;

        public UsersInactiveConsumer(IServiceScopeFactory scopeFactory, ILogger<UsersInactiveConsumer> logger, IConfiguration cfg)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _bootstrap = cfg["KAFKA_BOOTSTRAP_SERVERS"] ?? "kafka:9092";
            _groupId = cfg["KAFKA_USERS_INACTIVE_GROUP"] ?? "orders-users-inactive-consumer";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var conf = new ConsumerConfig
            {
                BootstrapServers = _bootstrap,
                GroupId = _groupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = true,
                EnablePartitionEof = true
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(conf)
                .SetLogHandler((_, m) => _logger.LogDebug("Kafka log: {Facility} {Message}", m.Facility, m.Message))
                .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Code} {Reason}", e.Code, e.Reason))
                .Build();

            try
            {
                _logger.LogInformation("UsersInactiveConsumer subscribing to {Topics} (bootstrap={Bootstrap})", string.Join(',', _topics), _bootstrap);
                consumer.Subscribe(_topics);

                // Optionally wait for topics to exist (best-effort)
                using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _bootstrap }).Build();
                try
                {
                    var md = admin.GetMetadata(TimeSpan.FromSeconds(5));
                    var missing = _topics.Where(t => !md.Topics.Any(x => string.Equals(x.Topic, t, StringComparison.OrdinalIgnoreCase) && !x.Error.IsError)).ToArray();
                    if (missing.Any())
                    {
                        _logger.LogWarning("Topics missing (users.inactive): {Missing} - continuing and relying on consumer to handle late creation", string.Join(',', missing));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not fetch metadata during startup; proceeding with consumer.");
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<Ignore, string>? cr = null;
                    try
                    {
                        cr = consumer.Consume(TimeSpan.FromSeconds(1));
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Kafka consume error");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected consume error");
                        await Task.Delay(1000, stoppingToken).ContinueWith(_ => { });
                        continue;
                    }

                    if (cr == null || cr.IsPartitionEOF) continue;

                    var payload = cr.Message?.Value;
                    if (string.IsNullOrWhiteSpace(payload))
                    {
                        _logger.LogWarning("Received empty payload on topic {Topic}", cr.Topic);
                        continue;
                    }

                    Guid userId;
                    DateTime occurredUtc = DateTime.UtcNow;

                    try
                    {
                        using var doc = JsonDocument.Parse(payload);
                        var root = doc.RootElement;

                        // extract userId (case-insensitive)
                        if (root.TryGetProperty("userId", out var uElem) && uElem.ValueKind == JsonValueKind.String && Guid.TryParse(uElem.GetString(), out userId))
                        {
                            // optional occurredAtUtc from message
                            if (root.TryGetProperty("occurredAtUtc", out var occurredElem) && occurredElem.ValueKind == JsonValueKind.String)
                            {
                                if (DateTime.TryParse(occurredElem.GetString(), out var parsed))
                                    occurredUtc = parsed.ToUniversalTime();
                            }
                        }
                        else
                        {
                            _logger.LogWarning("users.inactive message missing or invalid userId; skipping. Payload={Payload}", payload);
                            continue;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse users.inactive payload; skipping. Raw={Payload}", payload);
                        continue;
                    }

                    try
                    {
                        await HandleUserInactiveAsync(userId, occurredUtc, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error handling users.inactive for user {UserId}", userId);
                    }
                }
            }
            finally
            {
                try
                {
                    consumer.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception closing Kafka consumer");
                }
            }
        }

        private async Task HandleUserInactiveAsync(Guid userId, DateTime occurredAtUtc, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            // Find orders for the user that are eligible to be cancelled
            var eligibleStatuses = new[] { OrderStatus.PendingPayment, OrderStatus.Ready };
            var orders = await db.Orders
                .Where(o => o.UserId == userId && (o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.Ready))
                .ToListAsync(cancellationToken);

            if (orders.Count == 0)
            {
                _logger.LogDebug("No eligible orders to cancel for user {UserId}", userId);
                return;
            }

            _logger.LogInformation("Cancelling {Count} orders for inactive user {UserId}", orders.Count, userId);

            foreach (var order in orders)
            {
                // double-check idempotency: only change if still eligible
                if (order.Status != OrderStatus.PendingPayment && order.Status != OrderStatus.Ready)
                    continue;

                order.Status = OrderStatus.Cancelled;

                var payloadObj = new
                {
                    eventId = Guid.NewGuid(),
                    occurredAtUtc = occurredAtUtc,
                    orderId = order.Id,
                    userId = userId,
                    reason = "user_inactive"
                };

                var outbox = new OutboxEntry
                {
                    Id = Guid.NewGuid(),
                    EventType = "orders.cancelled",
                    AggregateId = order.Id,
                    Payload = JsonSerializer.Serialize(payloadObj),
                    RetryCount = 0,
                    CreatedAt = DateTime.UtcNow
                };

                db.OutboxEntries.Add(outbox);
            }

            // Persist status changes + outbox entries together
            var provider = db.Database.ProviderName ?? string.Empty;
            if (provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                }
                catch
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }

            _logger.LogInformation("Processed cancellation for user {UserId}: {Count} orders affected", userId, orders.Count);
        }
    }
}
