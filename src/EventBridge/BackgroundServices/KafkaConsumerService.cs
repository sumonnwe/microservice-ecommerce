using Confluent.Kafka;
using Confluent.Kafka.Admin;
using EventBridge.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EventBridge.BackgroundServices
{
    public class KafkaConsumerService : BackgroundService
    {
        private readonly ILogger<KafkaConsumerService> _logger;
        private readonly IHubContext<EventHub> _hub;
        private readonly string _bootstrap;
        private readonly string[] _topics = new[] { "users.created", "orders.created", "dead-letter", "users.status-changed", "orders.cancelled" };

        public KafkaConsumerService(ILogger<KafkaConsumerService> logger, IHubContext<EventHub> hub, IConfiguration cfg)
        {
            _logger = logger;
            _hub = hub;
            _bootstrap = cfg["KAFKA_BOOTSTRAP_SERVERS"] ?? "kafka:9092";
        }

        // (only the ExecuteAsync method body is shown — replace the existing metadata wait loop)
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

                // Create an admin client to fetch metadata (AdminClient exposes GetMetadata) and optionally create topics.
                using var admin = new AdminClientBuilder(conf).Build();

                // Wait for broker and topics to become available before entering main consume loop.
                var waitTimeout = TimeSpan.FromSeconds(60);
                var pollInterval = TimeSpan.FromSeconds(2);
                var sw = System.Diagnostics.Stopwatch.StartNew();

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
                            // topics that are absent or have an error
                            var missing = _topics.Where(t =>
                                !md.Topics.Any(x => string.Equals(x.Topic, t, StringComparison.OrdinalIgnoreCase) && !x.Error.IsError)
                            ).ToArray();

                            if (!missing.Any())
                            {
                                _logger.LogInformation("All topics available: {topics}", string.Join(',', _topics));
                                break;
                            }

                            _logger.LogWarning("Waiting for topics to become available: {missing}", string.Join(',', missing));

                            // Attempt to create missing topics (best-effort). This requires broker/admin privileges.
                            try
                            {
                                var specs = missing.Select(t => new TopicSpecification
                                {
                                    Name = t,
                                    NumPartitions = 1,
                                    ReplicationFactor = 1
                                }).ToList();

                                if (specs.Count > 0)
                                {
                                    _logger.LogInformation("Attempting to create missing topics: {topics}", string.Join(',', missing));
                                    try
                                    {
                                        // best-effort create
                                        admin.CreateTopicsAsync(specs).GetAwaiter().GetResult();
                                        _logger.LogInformation("CreateTopicsAsync completed for: {topics}", string.Join(',', missing));
                                    }
                                    catch (CreateTopicsException cte)
                                    {
                                        // log and continue — topics might already be being created or creation not allowed
                                        _logger.LogWarning(cte, "CreateTopicsException while creating topics; continuing to wait");
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Exception while attempting to create topics; continuing to wait");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error when attempting topic creation; will continue to wait");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error while fetching metadata; will retry");
                    }

                    if (sw.Elapsed > waitTimeout)
                    {
                        // Give up waiting — log and proceed. Consumer will still work once topics appear,
                        // or errors will be raised by the client. This avoids infinite blocking.
                        _logger.LogWarning("Timeout waiting for topics ({timeout}s). Proceeding and relying on consumer to handle late topic creation.", waitTimeout.TotalSeconds);
                        break;
                    }

                    try { await Task.Delay(pollInterval, stoppingToken); } catch (OperationCanceledException) { break; }
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
                        try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
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