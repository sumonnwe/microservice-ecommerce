using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OutboxDispatcher.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Shared.Domain.Entities;

namespace OutboxDispatcher.Worker
{
    // Polls services' /api/outbox/unsent and pushes messages to Kafka.
    public class OutboxWorker : BackgroundService
    {
        private readonly ILogger<OutboxWorker> _logger;
        private readonly HttpClient _http;
        private readonly ProducerConfig _producerConfig;
        private readonly string[] _serviceUrls;
        private readonly int _pollIntervalMs;
        private readonly int _maxRetries;
        private readonly IProducer<Null, string> _producer;

        public OutboxWorker(ILogger<OutboxWorker> logger, IHttpClientFactory httpFactory, IOptions<OutboxOptions> options)
        {
            _logger = logger;
            _http = httpFactory.CreateClient();

            var opts = options?.Value ?? new OutboxOptions();

            var kafka = opts.KafkaBootstrapServers ?? "kafka:9092";
            _serviceUrls = opts.ServiceUrls ?? new[] { "http://userservice:8080", "http://orderservice:8080" };
            _pollIntervalMs = opts.PollIntervalMs;
            _maxRetries = opts.MaxRetries;

            _producerConfig = new ProducerConfig { BootstrapServers = kafka };
            _producer = new ProducerBuilder<Null, string>(_producerConfig).Build();
        }

        public async Task PublishPendingAsync(int take, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OutboxDispatcher started. Polling services: {0}", string.Join(',', _serviceUrls));
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var baseUrl in _serviceUrls)
                {
                    try
                    {
                        var unsentUrl = $"{baseUrl.TrimEnd('/')}/api/outbox/unsent";
                        var entries = await _http.GetFromJsonAsync<List<Shared.Domain.Entities.OutboxEntry>>(unsentUrl, stoppingToken);
                        if (entries == null) continue;

                        foreach (var entry in entries)
                        {
                            try
                            {
                                // publish to Kafka using EventType as topic
                                await _producer.ProduceAsync(entry.EventType, new Message<Null, string> { Value = entry.Payload }, stoppingToken);

                                // mark sent
                                var markUrl = $"{baseUrl.TrimEnd('/')}/api/outbox/mark-sent/{entry.Id}";
                                await _http.PostAsync(markUrl, null, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to publish entry {id}", entry.Id);
                                var incUrl = $"{baseUrl.TrimEnd('/')}/api/outbox/increment-retry/{entry.Id}";
                                await _http.PostAsync(incUrl, null, stoppingToken);

                                if (entry.RetryCount + 1 >= _maxRetries)
                                {
                                    // send to dead-letter topic
                                    await _producer.ProduceAsync("dead-letter", new Message<Null, string> { Value = entry.Payload }, stoppingToken);
                                    // mark sent to avoid infinite loop
                                    var markUrl = $"{baseUrl.TrimEnd('/')}/api/outbox/mark-sent/{entry.Id}";
                                    await _http.PostAsync(markUrl, null, stoppingToken);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error polling {baseUrl}", baseUrl);
                    }
                }

                await Task.Delay(_pollIntervalMs, stoppingToken);
            }
        }
    }
}