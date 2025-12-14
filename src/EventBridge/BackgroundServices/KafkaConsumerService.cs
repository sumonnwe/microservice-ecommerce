using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR;
using EventBridge.Hubs;
using Microsoft.Extensions.Configuration;

namespace EventBridge.BackgroundServices
{
    public class KafkaConsumerService : BackgroundService
    {
        private readonly ILogger<KafkaConsumerService> _logger;
        private readonly IHubContext<EventHub> _hub;
        private readonly string _bootstrap;
        private readonly string[] _topics = new[] { "users.created", "orders.created", "dead-letter" };

        public KafkaConsumerService(ILogger<KafkaConsumerService> logger, IHubContext<EventHub> hub, IConfiguration cfg)
        {
            _logger = logger;
            _hub = hub;
            _bootstrap = cfg["KAFKA_BOOTSTRAP_SERVERS"] ?? "kafka:9092";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var conf = new ConsumerConfig
            {
                // connect to broker
                BootstrapServers = _bootstrap,

                // make group stable / configurable so it appears in Kafka UI
                GroupId = "eventBridge-group",

                AutoOffsetReset = AutoOffsetReset.Earliest,

                EnablePartitionEof = true,
                EnableAutoCommit = true,

                // 🔍 turn on deep debug
                Debug = "consumer,cgrp,topic,fetch,protocol,broker"
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(conf)
            .SetLogHandler((_, m) => _logger.LogInformation("Kafka log: {Facility} {Message}", m.Facility, m.Message))
            .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Code} {Reason}", e.Code, e.Reason))
            .SetPartitionsAssignedHandler((c, parts) =>
                _logger.LogInformation("✅ Assigned partitions: {Parts}", string.Join(", ", parts)))
            .SetPartitionsRevokedHandler((c, parts) =>
                _logger.LogInformation("❌ Revoked partitions: {Parts}", string.Join(", ", parts)))
            .Build();

            try
            {
                _logger.LogInformation("Kafka bootstrap servers: {bootstrap}", _bootstrap);

                // Subscribe early so partition assignment behaviour is correct, but wait for topics/broker availability before consuming.
                consumer.Subscribe(_topics);

                // Create an admin client to fetch metadata (AdminClient exposes GetMetadata).
                using var admin = new AdminClientBuilder(conf).Build();

                // Wait for broker and topics to become available before entering main consume loop.
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var md = admin.GetMetadata(TimeSpan.FromSeconds(5));
                        if (md.Brokers == null || md.Brokers.Count == 0)
                        {
                            _logger.LogWarning("No Kafka brokers available yet. Retrying...");
                        }
                        else
                        {
                            var missing = _topics.Where(t =>
                                !md.Topics.Any(x => string.Equals(x.Topic, t, StringComparison.OrdinalIgnoreCase) && !x.Error.IsError)
                            ).ToArray();

                            if (!missing.Any())
                            {
                                _logger.LogInformation("All topics available: {topics}", string.Join(',', _topics));
                                break;
                            }

                            _logger.LogWarning("Waiting for topics to become available: {missing}", string.Join(',', missing));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error while fetching metadata; will retry");
                    }

                    try { await Task.Delay(2000, stoppingToken); } catch (OperationCanceledException) { break; }
                }

                // Main consume loop. Close the consumer once, when shutting down.
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
                        _logger.LogError(ex, "Fatal consume error – continuing");
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    if (cr == null || cr.IsPartitionEOF)
                        continue;

                    var value = cr.Message?.Value;
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    try
                    {
                        await _hub.Clients.All.SendAsync(
                            "ReceiveEvent",
                            cr.Topic,
                            value,
                            cancellationToken: stoppingToken
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SignalR broadcast failed");
                    }
                }
            }
            finally
            {
                try
                {
                    // Ensure the consumer is closed exactly once.
                    consumer.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception during consumer.Close()");
                }
            }
        }
    }
}