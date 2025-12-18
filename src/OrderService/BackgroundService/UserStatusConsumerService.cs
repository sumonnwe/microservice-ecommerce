using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Shared.Domain.Events;

namespace OrderService.BackgroundServices
{
    // Background Kafka consumer that listens for users.status-changed and delegates to handler.
    // Uses Confluent.Kafka Consumer to satisfy requirement that consumers use Confluent.Kafka.
    //
    // Fix: consume scoped handler from a created scope per message instead of injecting
    // a scoped service into this singleton hosted service. This avoids the DI error:
    // "Cannot consume scoped service 'UserStatusChangedHandler' from singleton 'IHostedService'".
    public class UserStatusConsumerService : BackgroundService
    {
        private readonly ILogger<UserStatusConsumerService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private IConsumer<string, string>? _consumer;

        public UserStatusConsumerService(ILogger<UserStatusConsumerService> logger,
                                         IServiceProvider serviceProvider,
                                         IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            var bootstrap = _configuration["Kafka:BootstrapServers"]
                            ?? _configuration["KAFKA_BOOTSTRAP_SERVERS"]
                            ?? "kafka:9092";

            var groupId = _configuration["Kafka:GroupId"]
                          ?? _configuration["Kafka:UserStatusConsumerGroup"]
                          ?? "order-service-user-status-consumer";

            var conf = new ConsumerConfig
            {
                BootstrapServers = bootstrap,
                GroupId = groupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            _consumer = new ConsumerBuilder<string, string>(conf).SetErrorHandler((_, e) =>
            {
                _logger.LogError("Kafka consumer error: {Reason}", e.Reason);
            }).Build();

            // Read topics from configuration. Accept either an array under Kafka:Topics or the legacy single key.
            string[] topics;
            var topicsSection = _configuration.GetSection("Kafka:Topics");
            if (topicsSection.Exists())
            {
                topics = topicsSection.Get<string[]>() ?? new[] { "users.status-changed" };
            }
            else
            {
                var single = _configuration["Kafka:Topics:UserStatusChanged"]
                             ?? _configuration["Kafka:Topic"] // additional fallback
                             ?? "users.status-changed";
                topics = new[] { single };
            }

            // Wait for broker and topics to be available before subscribing (reduces noisy logs and avoids immediate connection refused).
            try
            {
                using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();
                var waitTimeout = TimeSpan.FromSeconds(60);
                var pollInterval = TimeSpan.FromSeconds(2);
                var sw = Stopwatch.StartNew();

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var md = admin.GetMetadata(TimeSpan.FromSeconds(5));
                        _logger.LogInformation("Kafka metadata brokers: {Brokers}", string.Join(", ", md.Brokers.Select(b => $"{b.BrokerId}:{b.Host}:{b.Port}")));
                        foreach (var b in md.Brokers)
                        {
                            _logger.LogInformation("Broker {Id} => {Host}:{Port}", b.BrokerId, b.Host, b.Port);
                        }
                        if (md?.Brokers == null || md.Brokers.Count == 0)
                        {
                            _logger.LogWarning("No Kafka brokers available yet at {Bootstrap}. Retrying...", bootstrap);
                        }
                        else
                        {
                            var missing = topics.Where(t =>
                                !md.Topics.Any(x => string.Equals(x.Topic, t, StringComparison.OrdinalIgnoreCase) && !x.Error.IsError)
                            ).ToArray();

                            if (!missing.Any())
                            {
                                _logger.LogInformation("Kafka reachable and topics available: {Topics}", string.Join(", ", topics));
                                break;
                            }

                            _logger.LogWarning("Waiting for topics to become available: {Missing}", string.Join(", ", missing));

                            // Attempt best-effort topic creation (requires broker/admin rights)
                            try
                            {
                                var specs = missing.Select(t => new TopicSpecification { Name = t, NumPartitions = 1, ReplicationFactor = 1 }).ToList();
                                if (specs.Count > 0)
                                {
                                    _logger.LogInformation("Attempting to create missing topics: {Missing}", string.Join(", ", missing));
                                    try
                                    {
                                        admin.CreateTopicsAsync(specs).GetAwaiter().GetResult();
                                        _logger.LogInformation("CreateTopicsAsync requested for: {Missing}", string.Join(", ", missing));
                                    }
                                    catch (CreateTopicsException cte)
                                    {
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
                        _logger.LogWarning(ex, "Error while fetching Kafka metadata; retrying...");
                    }

                    if (sw.Elapsed > waitTimeout)
                    {
                        _logger.LogWarning("Timeout waiting for Kafka metadata after {Seconds}s; proceeding and relying on consumer to handle late topic creation.", waitTimeout.TotalSeconds);
                        break;
                    }

                    try { Task.Delay(pollInterval, cancellationToken).Wait(cancellationToken); } catch (OperationCanceledException) { break; }
                }
            }
            catch (Exception ex)
            {
                // If admin client can't be created, log and proceed — consumer will still attempt to connect.
                _logger.LogWarning(ex, "Admin client creation/metadata check failed for bootstrap={Bootstrap}. Consumer will still attempt to connect.", bootstrap);
            }

            _consumer.Subscribe(topics);

            _logger.LogInformation("UserStatusConsumerService subscribed to topics: {Topics} (bootstrap: {Bootstrap})", string.Join(", ", topics), bootstrap);

            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_consumer == null)
            {
                _logger.LogError("Kafka consumer is not initialized.");
                return;
            }

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cr = _consumer.Consume(stoppingToken);
                    if (cr == null) continue;

                    _logger.LogDebug("Consumed message from topic {Topic} partition {Partition} offset {Offset}", cr.Topic, cr.Partition, cr.Offset);

                    try
                    {
                        var value = cr.Message?.Value;
                        if (string.IsNullOrEmpty(value))
                        {
                            _logger.LogWarning("Received empty message for users.status-changed.");
                            _consumer.Commit(cr);
                            continue;
                        }

                        var evt = JsonSerializer.Deserialize<UserStatusChangedEvent>(value, jsonOptions);
                        if (evt == null)
                        {
                            _logger.LogWarning("Failed to deserialize UserStatusChangedEvent: {Value}", value);
                            _consumer.Commit(cr);
                            continue;
                        }

                        // Resolve a scoped handler from a scope created per message to avoid
                        // injecting a scoped service into this singleton hosted service.
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var handler = scope.ServiceProvider.GetRequiredService<OrderService.Handlers.UserStatusChangedHandler>();
                            await handler.HandleAsync(evt, stoppingToken);
                        }

                        // Commit offset only after successful handling
                        _consumer.Commit(cr);
                    }
                    catch (OperationCanceledException)
                    {
                        // shutdown requested
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception while processing user status change message.");
                        // Do NOT commit so message can be retried depending on consumer group semantics.
                    }
                }
                catch (ConsumeException cex)
                {
                    _logger.LogError(cex, "Error consuming Kafka message: {Reason}", cex.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // graceful shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in consumer loop.");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            if (_consumer != null)
            {
                try
                {
                    _consumer.Close();
                    _consumer.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception closing Kafka consumer.");
                }
            }

            return base.StopAsync(cancellationToken);
        }
    }
}
