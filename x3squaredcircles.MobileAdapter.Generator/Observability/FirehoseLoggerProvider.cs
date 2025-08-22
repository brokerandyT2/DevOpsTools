using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;

namespace x3squaredcircles.MobileAdapter.Generator.Observability
{
    /// <summary>
    /// An ILoggerProvider that creates instances of FirehoseLogger. It manages the lifecycle
    /// of the background thread that batches and sends log messages to the firehose endpoint.
    /// </summary>
    public sealed class FirehoseLoggerProvider : ILoggerProvider
    {
        private readonly GeneratorConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BlockingCollection<FirehoseLogMessage> _logQueue = new(new ConcurrentQueue<FirehoseLogMessage>());
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task _processingTask;

        public FirehoseLoggerProvider(GeneratorConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;

            if (IsConfigured())
            {
                _processingTask = Task.Run(ProcessLogQueue, _cancellationTokenSource.Token);
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FirehoseLogger(categoryName, this);
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_config.Observability.FirehoseLogEndpointUrl) &&
                   !string.IsNullOrWhiteSpace(_config.Observability.FirehoseLogEndpointToken);
        }

        internal void PostMessage(FirehoseLogMessage message)
        {
            // Don't block the calling thread if the queue is full. This is a "best effort" logger.
            _logQueue.TryAdd(message);
        }

        private async Task ProcessLogQueue()
        {
            var client = _httpClientFactory.CreateClient("FirehoseLogger");
            client.BaseAddress = new Uri(_config.Observability.FirehoseLogEndpointUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.Observability.FirehoseLogEndpointToken);

            var batch = new List<FirehoseLogMessage>();

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    // Block and wait for the first message
                    var message = _logQueue.Take(_cancellationTokenSource.Token);
                    batch.Add(message);

                    // Grab any other messages that have arrived in the meantime to create a batch
                    while (batch.Count < 50 && _logQueue.TryTake(out var additionalMessage, TimeSpan.FromMilliseconds(200)))
                    {
                        batch.Add(additionalMessage);
                    }

                    if (batch.Any())
                    {
                        await SendBatchAsync(client, batch);
                        batch.Clear();
                    }
                }
                catch (OperationCanceledException)
                {
                    // This is expected when the application is shutting down.
                    break;
                }
                catch (Exception ex)
                {
                    // Log to console as a last resort if the logging pipeline itself fails.
                    Console.WriteLine($"[ERROR] Unhandled exception in FirehoseLoggerProvider: {ex.Message}");
                    await Task.Delay(5000); // Prevent fast failure loops
                }
            }

            // Process any remaining items in the queue before exiting
            if (_logQueue.Any())
            {
                batch.AddRange(_logQueue.GetConsumingEnumerable());
                await SendBatchAsync(client, batch);
            }
        }

        private async Task SendBatchAsync(HttpClient client, List<FirehoseLogMessage> batch)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                };
                var jsonPayload = JsonSerializer.Serialize(batch, options);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await client.PostAsync("", content, timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[WARN] Firehose endpoint returned a non-success status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to send log batch to firehose: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _processingTask?.Wait(TimeSpan.FromSeconds(5)); // Give it a moment to finish
            _logQueue.Dispose();
            _cancellationTokenSource.Dispose();
        }
    }
}