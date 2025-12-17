using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Infrastructure.EF;
using Shared.Domain.Entities;
using OrderService.Domain.Entities;

namespace OrderService.BackgroundServices
{
    /// <summary>
    /// Background worker that expires orders which passed their ExpiresAtUtc.
    /// Scans every 5 seconds and processes up to a batch of 50 orders per iteration.
    /// </summary>
    public class OrderExpiryWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderExpiryWorker> _logger;
        private const int PollDelayMs = 5000;
        private const int BatchSize = 50;

        public OrderExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<OrderExpiryWorker> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OrderExpiryWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Open a scope to resolve a DbContext
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

                    var now = DateTime.UtcNow;

                    // Query a batch of orders that are pending payment OR ready and have expired
                    var expiredOrders = await db.Orders
                        .Where(o => (o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.Ready)
                                    && o.ExpiresAtUtc <= now)
                        .OrderBy(o => o.ExpiresAtUtc)
                        .Take(BatchSize)
                        .ToListAsync(stoppingToken);

                    if (expiredOrders.Count == 0)
                    {
                        // nothing to do this cycle
                        await Task.Delay(PollDelayMs, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Found {Count} expired orders to mark Expired.", expiredOrders.Count);

                    // Persist updates in a single transactional batch
                    var provider = db.Database.ProviderName ?? string.Empty;
                    if (provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var order in expiredOrders)
                        {
                            await ProcessSingleOrderAsync(db, order, now, stoppingToken);
                        }

                        await db.SaveChangesAsync(stoppingToken);
                    }
                    else
                    {
                        await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);
                        try
                        {
                            foreach (var order in expiredOrders)
                            {
                                await ProcessSingleOrderAsync(db, order, now, stoppingToken);
                            }

                            await db.SaveChangesAsync(stoppingToken);
                            await tx.CommitAsync(stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            // respect cancellation
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error while processing expired orders; rolling back transaction.");
                            await tx.RollbackAsync(CancellationToken.None);
                            // swallow and continue loop after a short delay to avoid tight exception loop
                            await Task.Delay(PollDelayMs, stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // shutdown requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in OrderExpiryWorker loop.");
                    // wait a bit before retrying to avoid tight error loop
                    try { await Task.Delay(PollDelayMs, stoppingToken); } catch { /* ignored */ }
                }
            }

            _logger.LogInformation("OrderExpiryWorker stopping.");
        }

        private static Task ProcessSingleOrderAsync(OrderDbContext db, Order order, DateTime occurredAtUtc, CancellationToken cancellationToken)
        {
            // mark order expired
            order.Status = OrderStatus.Expired;

            // build outbox payload
            var payloadObj = new
            {
                eventId = Guid.NewGuid(),
                occurredAtUtc = occurredAtUtc,
                orderId = order.Id,
                userId = order.UserId,
                reason = "timeout"
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

            // return completed task (actual SaveChanges is done by caller)
            return Task.CompletedTask;
        }
    }
}
