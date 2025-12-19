using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.OutboxPublisher.Abstractions;
using Shared.OutboxPublisher.Models;

namespace Shared.OutboxPublisher.Publishers
{
    public sealed class KafkaOutboxPublisher : IOutboxPublisher, IDisposable
    {
        private readonly IProducer<Null, string> _producer;
        private readonly OutboxPublisherOptions _options;
        private readonly ILogger<KafkaOutboxPublisher> _logger;
        private bool _disposed;

        public KafkaOutboxPublisher(IProducer<Null, string> producer, IOptions<OutboxPublisherOptions> options, ILogger<KafkaOutboxPublisher> logger)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PublishResult> PublishAsync(OutboxEntry entry, CancellationToken cancellationToken)
        {
            if (entry is null) throw new ArgumentNullException(nameof(entry));

            // If entry exceeded retries we treat as permanent failure: move to dead-letter topic
            if (entry.RetryCount >= _options.MaxRetries)
            {
                try
                {
                    var deadValue = JsonSerializer.Serialize(new
                    {
                        entry.Id,
                        entry.EventType,
                        entry.Payload,
                        entry.RetryCount,
                        entry.OccurredAt,
                        Reason = "MaxRetriesExceeded"
                    });

                    var dr = await _producer.ProduceAsync(_options.DeadLetterTopic, new Message<Null, string> { Value = deadValue }, cancellationToken).ConfigureAwait(false);
                    _logger.LogWarning("Outbox entry {EntryId} moved to dead-letter topic {DeadLetterTopic} at partition {Partition} / offset {Offset}.",
                        entry.Id, _options.DeadLetterTopic, dr.Partition.Value, dr.Offset.Value);

                    return new PublishResult { Success = false, PermanentlyFailed = true };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish outbox entry {EntryId} to dead-letter topic {DeadLetterTopic}.", entry.Id, _options.DeadLetterTopic);
                    return new PublishResult { Success = false, PermanentlyFailed = true, Error = ex.Message };
                }
            }

            // Normal publish to event topic (EventType)
            try
            {
                var value = entry.Payload;
                var topic = entry.EventType ?? throw new InvalidOperationException("EventType must be set on OutboxEntry.");

                var dr = await _producer.ProduceAsync(topic, new Message<Null, string> { Value = value }, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Published outbox entry {EntryId} to topic {Topic} at partition {Partition} / offset {Offset}.",
                    entry.Id, topic, dr.Partition.Value, dr.Offset.Value);

                return new PublishResult { Success = true };
            }
            catch (ProduceException<Null, string> pex)
            {
                _logger.LogWarning(pex, "Kafka produce exception for entry {EntryId} into event {EventType}.", entry.Id, entry.EventType);
                return new PublishResult { Success = false, PermanentlyFailed = false, Error = pex.Message };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error publishing outbox entry {EntryId}.", entry.Id);
                return new PublishResult { Success = false, PermanentlyFailed = false, Error = ex.Message };
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                _producer.Flush(TimeSpan.FromSeconds(5));
                _producer.Dispose();
            }
            catch
            {
                // swallow on dispose
            }
            _disposed = true;
        }
    }
}