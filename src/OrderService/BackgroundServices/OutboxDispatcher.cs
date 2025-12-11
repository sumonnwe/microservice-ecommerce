using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Domain.Services;
using Polly;
using Polly.Retry;
using System.Linq;

namespace OrderService.BackgroundServices
{
    public class OutboxDispatcher : BackgroundService
    {
        private readonly ILogger<OutboxDispatcher> _logger;
        private readonly IOutboxRepository _outbox;
        private readonly IKafkaProducer _producer;
        private readonly int _maxRetries = 5;
        private readonly AsyncRetryPolicy _publishRetry;

        public OutboxDispatcher(ILogger<OutboxDispatcher> logger, IOutboxRepository outbox, IKafkaProducer producer)
        {
            _logger = logger;
            _outbox = outbox;
            _producer = producer;

            _publishRetry = Policy.Handle<Exception>().WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (ex, ts) => _logger.LogWarning(ex, "Publish retry in {delay}", ts)
            );
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OutboxDispatcher started for OrderService");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var entries = await _outbox.GetUnsentAsync(_maxRetries);
                    foreach (var e in entries.OrderBy(x => x.CreatedAt))
                    {
                        try
                        {
                            await _publishRetry.ExecuteAsync(() => _producer.ProduceAsync(e.EventType, e.Payload));
                            await _outbox.MarkAsSentAsync(e.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to publish outbox {id}", e.Id);
                            await _outbox.IncrementRetryAsync(e.Id);
                            if (e.RetryCount + 1 >= _maxRetries)
                            {
                                try
                                {
                                    await _producer.ProduceAsync("dead-letter", e.Payload);
                                    await _outbox.MarkAsSentAsync(e.Id);
                                }
                                catch (Exception dlqEx)
                                {
                                    _logger.LogError(dlqEx, "Failed to send DLQ for {id}", e.Id);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OutboxDispatcher loop error (orders)");
                }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
