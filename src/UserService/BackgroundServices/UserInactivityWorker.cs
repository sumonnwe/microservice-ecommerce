using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Domain.Entities;
using UserService.Infrastructure.EF;

namespace UserService.BackgroundServices
{
    /// <summary>
    /// Background worker that detects inactive users and emits a single "users.inactive" outbox event per user.
    /// - Poll interval: default 10 seconds
    /// - Inactivity threshold: default 15 minutes (config key: Users:InactivityThresholdMinutes)
    /// </summary>
    public class UserInactivityWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UserInactivityWorker> _logger;
        private readonly int _pollIntervalSeconds;
        private readonly int _thresholdMinutes;
        private const int DefaultPollIntervalSeconds = 10;
        private const int DefaultThresholdMinutes = 15;
        private const int BatchSize = 50;

        public UserInactivityWorker(IServiceScopeFactory scopeFactory, ILogger<UserInactivityWorker> logger, IConfiguration? cfg = null)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pollIntervalSeconds = cfg?.GetValue<int>("Users:InactivityWorkerPollIntervalSeconds", DefaultPollIntervalSeconds) ?? DefaultPollIntervalSeconds;
            _thresholdMinutes = cfg?.GetValue<int>("Users:InactivityThresholdMinutes", DefaultThresholdMinutes) ?? DefaultThresholdMinutes;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UserInactivityWorker started (threshold={Minutes}m, poll={Seconds}s)", _thresholdMinutes, _pollIntervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();

                    var now = DateTime.UtcNow;
                    var cutoff = now.AddMinutes(-_thresholdMinutes);

                    // 1) Clear InactiveSinceUtc for users who became active again
                    var reactivated = await db.Users
                        .Where(u => u.InactiveSinceUtc != null && u.LastSeenUtc != null && u.LastSeenUtc > u.InactiveSinceUtc)
                        .ToListAsync(stoppingToken);

                    if (reactivated.Count > 0)
                    {
                        foreach (var u in reactivated)
                        {
                            _logger.LogInformation("User reactivated, clearing InactiveSinceUtc: {UserId}", u.Id);
                            u.InactiveSinceUtc = null;
                        }

                        await db.SaveChangesAsync(stoppingToken);
                    }

                    // 2) Find users that passed inactivity threshold and haven't been marked inactive yet
                    var candidates = await db.Users
                        .Where(u => u.LastSeenUtc != null
                                    && u.InactiveSinceUtc == null
                                    && u.LastSeenUtc <= cutoff
                                    && u.IsActive) // only consider currently active users
                        .OrderBy(u => u.LastSeenUtc)
                        .Take(BatchSize)
                        .ToListAsync(stoppingToken);

                    if (candidates.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Found {Count} inactive users to mark and emit events for.", candidates.Count);

                    // Persist updates (mark InactiveSinceUtc and create Outbox entries) in a transaction when supported
                    var provider = db.Database.ProviderName ?? string.Empty;
                    if (provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var u in candidates)
                            await ProcessUserAsync(db, u, now, stoppingToken);

                        await db.SaveChangesAsync(stoppingToken);
                    }
                    else
                    {
                        await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);
                        try
                        {
                            foreach (var u in candidates)
                                await ProcessUserAsync(db, u, now, stoppingToken);

                            await db.SaveChangesAsync(stoppingToken);
                            await tx.CommitAsync(stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error while processing inactive users; rolling back.");
                            await tx.RollbackAsync(CancellationToken.None);
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
                    _logger.LogError(ex, "Unhandled error in UserInactivityWorker loop.");
                }

                try { await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken); } catch { /* ignore */ }
            }

            _logger.LogInformation("UserInactivityWorker stopping.");
        }

        private static Task ProcessUserAsync(UserDbContext db, Domain.Entities.User user, DateTime occurredAtUtc, CancellationToken cancellationToken)
        {
            // Mark when we detected inactivity so we don't emit duplicate events on subsequent loops.
            user.InactiveSinceUtc = occurredAtUtc;

            // prepare outbox payload
            var inactiveForSeconds = (int)Math.Max(0, (occurredAtUtc - (user.LastSeenUtc ?? occurredAtUtc)).TotalSeconds);

            var payloadObj = new
            {
                eventId = Guid.NewGuid(),
                occurredAtUtc = occurredAtUtc,
                userId = user.Id,
                inactiveForSeconds = inactiveForSeconds
            };

            var outbox = new OutboxEntry
            {
                Id = Guid.NewGuid(),
                EventType = "users.inactive",
                AggregateId = user.Id,
                Payload = JsonSerializer.Serialize(payloadObj),
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            db.OutboxEntries.Add(outbox);

            return Task.CompletedTask;
        }
    }
}
