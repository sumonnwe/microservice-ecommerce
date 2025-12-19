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
using OrderService.Configuration;

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

        private readonly string _bootstrap;
        private readonly string[] _topics;
        private readonly string _groupId;

        public UserStatusConsumerService(ILogger<UserStatusConsumerService> logger,
                                         IServiceProvider serviceProvider,
                                         IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;

            // Read Kafka config defensively so readonly fields are initialized.
            var kafkaSection = _configuration.GetSection("Kafka");
            OrderService.Configuration.KafkaOptions? kafkaOptions = null;
            try
            {
                if (kafkaSection.Exists())
                {
                    kafkaOptions = kafkaSection.Get<OrderService.Configuration.KafkaOptions>();
                }
            }
            catch
            {
                // ignore binding errors; we'll fall back to defaults below
            }

            _bootstrap = _configuration["KAFKA_BOOTSTRAP_SERVERS"]
                         ?? kafkaOptions?.BootstrapServers
                         ?? _configuration["Kafka:BootstrapServers"]
                         ?? "kafka:9092";

            // Topics: prefer explicit array from configuration or KafkaOptions; fall back to legacy single keys or defaults.
            if (kafkaOptions?.Topics != null && kafkaOptions.Topics.Length > 0)
            {
                _topics = kafkaOptions.Topics;
            }
            else
            {
                var topicsSection = _configuration.GetSection("Kafka:Topics");
                if (topicsSection.Exists())
                {
                    _topics = topicsSection.Get<string[]>() ?? new[] { "users.status-changed" };
                }
                else
                {
                    var single = _configuration["Kafka:Topics:UserStatusChanged"]
                                 ?? _configuration["Kafka:Topic"]
                                 ?? _configuration["KAFKA_TOPICS"]
                                 ?? "users.status-changed";

                    _topics = single.Contains(',')
                        ? single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        : new[] { single };
                }
            }

            _groupId = _configuration["Kafka:GroupId"]
                       ?? kafkaOptions?.GroupId
                       ?? _configuration["Kafka:UserStatusConsumerGroup"]
                       ?? "orderService-group";
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            // Log startup config for easier debugging.
            _logger.LogInformation("Kafka bootstrap={Bootstrap} groupId={GroupId} topics=[{Topics}]",
                _bootstrap, _groupId, string.Join(";", _topics));

            // Quick validation of topics to avoid immediate librdkafka rejection.
            var invalid = _topics.Where(t =>
                string.IsNullOrWhiteSpace(t) ||
                t == "." || t == ".." ||
                t.Length > 249 ||
                t.IndexOfAny(new[] { ' ', '/', '\\' }) >= 0
            ).ToArray();

            if (invalid.Length > 0)
            {
                _logger.LogWarning("Invalid Kafka topic names detected: {Invalid}. Consumer will not subscribe.", string.Join(", ", invalid));
                return base.StartAsync(cancellationToken);
            }

            // Quick TCP check to provide actionable logs; do not throw to avoid stopping the host.
            bool TcpConnectTest(string host, int port, TimeSpan timeout)
            {
                try
                {
                    using var tcp = new System.Net.Sockets.TcpClient();
                    var ar = tcp.BeginConnect(host, port, null, null);
                    var ok = ar.AsyncWaitHandle.WaitOne(timeout);
                    if (!ok) return false;
                    tcp.EndConnect(ar);
                    return true;
                }
                catch { return false; }
            }

            var parts = _bootstrap.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var host = parts.Length > 0 ? parts[0] : "kafka";
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 9092;

            if (!TcpConnectTest(host, port, TimeSpan.FromSeconds(3)))
            {
                _logger.LogWarning("TCP connect to Kafka bootstrap {Bootstrap} FAILED. Check Docker network / DNS / advertised.listeners / port mapping. Consumer will not subscribe now.", _bootstrap);
                return base.StartAsync(cancellationToken);
            }

            // Build a consumer and subscribe — still guarded with try/catch to avoid crashing host.
            try
            {
                var conf = new ConsumerConfig
                {
                    BootstrapServers = _bootstrap,
                    GroupId = _groupId,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    EnableAutoCommit = false
                };

                _consumer = new ConsumerBuilder<string, string>(conf)
                    .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason}", e.Reason))
                    .SetPartitionsAssignedHandler((c, parts) =>
                        _logger.LogInformation("Assigned partitions: {Parts}", string.Join(", ", parts)))
                    .SetPartitionsRevokedHandler((c, parts) =>
                        _logger.LogInformation("Revoked partitions: {Parts}", string.Join(", ", parts)))
                    .Build();

                _consumer.Subscribe(_topics);
                _logger.LogInformation("UserStatusConsumerService subscribed to topics: {Topics} (bootstrap: {Bootstrap})", string.Join(", ", _topics), _bootstrap);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create/subscribe consumer. Consumer will not be active; check Kafka bootstrap and topic names. Host will remain running.");
                // ensure no partially initialized consumer remains
                try
                {
                    _consumer?.Close();
                    _consumer?.Dispose();
                }
                catch { }
                _consumer = null;
            }

            return base.StartAsync(cancellationToken);
        }

        private (string bootstrap, string groupId, string[] topics) ReadKafkaConfig()
        {
            // Provide runtime read for ExecuteAsync retries (will fallback to fields set in ctor)
            var kafkaSection = _configuration.GetSection("Kafka");
            OrderService.Configuration.KafkaOptions? kafkaOptions = null;
            try
            {
                if (kafkaSection.Exists())
                {
                    kafkaOptions = kafkaSection.Get<OrderService.Configuration.KafkaOptions>();
                }
            }
            catch { }

            var bootstrap = _configuration["KAFKA_BOOTSTRAP_SERVERS"]
                    ?? kafkaOptions?.BootstrapServers
                    ?? _bootstrap
                    ?? "kafka:9092";

            var groupId = _configuration["Kafka:GroupId"]
                          ?? kafkaOptions?.GroupId
                          ?? _groupId
                          ?? "order-service-user-status-consumer";

            string[] topics;
            var topicsSection = _configuration.GetSection("Kafka:Topics");
            if (topicsSection.Exists())
            {
                topics = topicsSection.Get<string[]>() ?? new[] { "users.status-changed" };
            }
            else
            {
                var single = _configuration["Kafka:Topics:UserStatusChanged"]
                             ?? _configuration["Kafka:Topic"]
                             ?? _configuration["KAFKA_TOPICS"]
                             ?? "users.status-changed";
                topics = single.Contains(',')
                    ? single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : new[] { single };
            }

            return (bootstrap, groupId, topics);
        }

        private static bool TcpConnectTest(string host, int port, TimeSpan timeout)
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var ar = tcp.BeginConnect(host, port, null, null);
                var ok = ar.AsyncWaitHandle.WaitOne(timeout);
                if (!ok) return false;
                tcp.EndConnect(ar);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ValidateTopics(string[] topics, out string[] invalid)
        {
            invalid = topics.Where(t =>
                string.IsNullOrWhiteSpace(t) ||
                t == "." || t == ".." ||
                t.Length > 249 ||
                t.IndexOfAny(new[] { ' ', '/', '\\' }) >= 0
            ).ToArray();

            return invalid.Length == 0;
        }

        private bool TryCreateAndSubscribeConsumer(string bootstrap, string groupId, string[] topics)
        {
            try
            {
                var conf = new ConsumerConfig
                {
                    BootstrapServers = bootstrap,
                    GroupId = groupId,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    EnableAutoCommit = false
                };

                _consumer = new ConsumerBuilder<string, string>(conf)
                    .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason}", e.Reason))
                    .SetPartitionsAssignedHandler((c, parts) =>
                        _logger.LogInformation("Assigned partitions: {Parts}", string.Join(", ", parts)))
                    .SetPartitionsRevokedHandler((c, parts) =>
                        _logger.LogInformation("Revoked partitions: {Parts}", string.Join(", ", parts)))
                    .Build();

                _consumer.Subscribe(topics);
                _logger.LogInformation("UserStatusConsumerService subscribed to topics: {Topics} (bootstrap: {Bootstrap})", string.Join(", ", topics), bootstrap);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create/subscribe consumer. Will retry later.");
                try
                {
                    _consumer?.Close();
                    _consumer?.Dispose();
                }
                catch { }
                _consumer = null;
                return false;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Retry loop: attempt to initialize consumer until successful or cancellation requested.
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_consumer == null)
                {
                    var (bootstrap, groupId, topics) = ReadKafkaConfig();
                    _logger.LogInformation("ExecuteAsync attempting Kafka init: bootstrap={Bootstrap} groupId={GroupId} topics=[{Topics}]", bootstrap, groupId, string.Join(";", topics));

                    if (!ValidateTopics(topics, out var invalid))
                    {
                        _logger.LogError("Invalid Kafka topic names detected: {Invalid}. Consumer will not be initialized until config is fixed.", string.Join(", ", invalid));
                        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { break; }
                        continue;
                    }

                    var parts = bootstrap.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var host = parts.Length > 0 ? parts[0] : "kafka";
                    var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 9092;

                    if (!TcpConnectTest(host, port, TimeSpan.FromSeconds(3)))
                    {
                        _logger.LogWarning("TCP connect to Kafka bootstrap {Bootstrap} FAILED. Check Docker network / DNS / advertised.listeners / port mapping. Retrying...", bootstrap);
                        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { break; }
                        continue;
                    }

                    try
                    {
                        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();
                        var md = admin.GetMetadata(TimeSpan.FromSeconds(5));
                        if (md?.Brokers == null || md.Brokers.Count == 0)
                        {
                            _logger.LogWarning("Admin client reports no brokers yet at {Bootstrap}. Retrying...", bootstrap);
                            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { break; }
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Admin metadata check failed (transient). Retrying...");
                        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { break; }
                        continue;
                    }

                    if (!TryCreateAndSubscribeConsumer(bootstrap, groupId, topics))
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { break; }
                        continue;
                    }
                }

                // If consumer is now initialized, enter consume loop
                try
                {
                    var cr = _consumer.Consume(TimeSpan.FromSeconds(1));
                    if (cr == null) continue;

                    if (cr.IsPartitionEOF) continue;

                    _logger.LogDebug("Consumed message from topic {Topic} partition {Partition} offset {Offset}", cr.Topic, cr.Partition, cr.Offset);

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

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var handler = scope.ServiceProvider.GetRequiredService<OrderService.Handlers.UserStatusChangedHandler>();
                        await handler.HandleAsync(evt, stoppingToken);
                    }

                    _consumer.Commit(cr);
                }
                catch (ConsumeException cex)
                {
                    _logger.LogError(cex, "Error consuming Kafka message: {Reason}", cex.Error.Reason);
                    try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); } catch (OperationCanceledException) { break; }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in consumer loop.");
                    try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); } catch (OperationCanceledException) { break; }
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