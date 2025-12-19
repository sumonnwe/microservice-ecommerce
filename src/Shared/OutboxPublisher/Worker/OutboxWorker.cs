using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.OutboxPublisher.Abstractions;
using Shared.OutboxPublisher.Models;

namespace Shared.OutboxPublisher.Worker
{
    /// <summary>
    /// Background service that repeatedly acquires a batch of pending messages, publishes them,
    /// and updates their status in the store. The worker resolves the scoped IOutboxStore from
    /// an IServiceProvider scope inside the processing loop so it can be registered as a singleton
    /// hosted service while the store (EF) remains scoped.
    /// </summary>
    internal sealed class OutboxPublisherWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IOutboxPublisher _publisher;
        private readonly OutboxPublisherOptions _options;
        private readonly ILogger<OutboxPublisherWorker> _logger;

        public OutboxPublisherWorker(IServiceProvider serviceProvider, IOutboxPublisher publisher, IOptions<OutboxPublisherOptions> options, ILogger<OutboxPublisherWorker> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OutboxPublisherWorker starting. PollIntervalMs={PollIntervalMs}, BatchSize={BatchSize}", _options.PollIntervalMs, _options.BatchSize);

            var lockDuration = TimeSpan.FromSeconds(Math.Max(1, _options.LockDurationSeconds));
            var pollDelay = TimeSpan.FromMilliseconds(Math.Max(1, _options.PollIntervalMs));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Resolve a scoped IOutboxStore for this iteration so EF DbContext (scoped) is used correctly.
                    using var scope = _serviceProvider.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

                    var entries = await store.AcquireAndLockAsync(_options.BatchSize, lockDuration, stoppingToken).ConfigureAwait(false);

                    if (entries == null || entries.Count == 0)
                    {
                        await Task.Delay(pollDelay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    _logger.LogInformation("Acquired {Count} outbox entries for publishing.", entries.Count);

                    foreach (var entry in entries.ToList()) // snapshot to avoid mutation issues
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        try
                        {
                            var result = await _publisher.PublishAsync(entry, stoppingToken).ConfigureAwait(false);

                            if (result.Success)
                            {
                                await store.MarkSentAsync(entry.Id, stoppingToken).ConfigureAwait(false);
                                _logger.LogInformation("Marked outbox entry {EntryId} as sent.", entry.Id);
                            }
                            else
                            {
                                var nextRetry = entry.RetryCount + 1;
                                await store.MarkFailedAsync(entry.Id, nextRetry, result.Error ?? "Publish failed", result.PermanentlyFailed, stoppingToken).ConfigureAwait(false);

                                if (result.PermanentlyFailed)
                                    _logger.LogWarning("Outbox entry {EntryId} permanently failed and was moved to dead-letter.", entry.Id);
                                else
                                    _logger.LogWarning("Outbox entry {EntryId} failed (retry {Retry}/{MaxRetries}). Error: {Error}", entry.Id, nextRetry, _options.MaxRetries, result.Error);
                            }
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            // shutdown requested
                            _logger.LogInformation("Cancellation requested while processing entry {EntryId}.", entry.Id);
                            break;
                        }
                        catch (Exception ex)
                        {
                            // Ensure one bad message doesn't crash the worker. Mark as failed with incremented retry.
                            _logger.LogError(ex, "Unhandled exception while publishing entry {EntryId}. Marking as failed.", entry.Id);
                            try
                            {
                                var nextRetry = entry.RetryCount + 1;
                                var permanentlyFailed = nextRetry > _options.MaxRetries;
                                await store.MarkFailedAsync(entry.Id, nextRetry, ex.Message, permanentlyFailed, stoppingToken).ConfigureAwait(false);
                            }
                            catch (Exception markEx)
                            {
                                _logger.LogError(markEx, "Failed to mark outbox entry {EntryId} as failed after publish exception.", entry.Id);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in outbox processing loop. Will retry after delay.");
                    try
                    {
                        await Task.Delay(pollDelay, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                }
            }

            _logger.LogInformation("OutboxPublisherWorker stopping.");
        }
    }
}
