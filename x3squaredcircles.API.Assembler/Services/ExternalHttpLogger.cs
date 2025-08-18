using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// A background service that sends structured log events to a configured external HTTP endpoint.
    /// This provides a "firehose" of operational logs for enterprise observability platforms like Splunk or ELK.
    /// </summary>
    public class ExternalHttpLogger : ILogger, IDisposable
    {
        private readonly string _name;
        private readonly HttpClient _httpClient;
        private readonly BlockingCollection<string> _logQueue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private readonly Task _processingTask;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public ExternalHttpLogger(string name, AssemblerConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _name = name;
            _httpClient = httpClientFactory.CreateClient("ExternalLogger");

            var endpoint = config.Logging.ExternalLogEndpoint;
            var token = config.Logging.ExternalLogToken; // Assumes token is already resolved from vault if needed

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("ExternalHttpLogger cannot be initialized without both an endpoint and a token.");
            }

            _httpClient.BaseAddress = new Uri(endpoint);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            _processingTask = Task.Run(ProcessLogQueueAsync, _cancellationTokenSource.Token);
        }

        public IDisposable BeginScope<TState>(TState state) => default!;

        public bool IsEnabled(LogLevel logLevel) => true; // All logs are passed to the queue

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // Do not block the logging thread. If the queue is full, we drop the log.
            _logQueue.TryAdd(FormatLogEntry(logLevel, eventId, state, exception, formatter));
        }

        private string FormatLogEntry<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var logPayload = new
            {
                Timestamp = DateTime.UtcNow,
                Level = logLevel.ToString(),
                Tool = "3sc-api-assembler",
                EventId = eventId.Id,
                Message = formatter(state, exception),
                Exception = exception?.ToString(),
                // Structured logging state can be included here
                State = state?.ToString()
            };

            return JsonSerializer.Serialize(logPayload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        private async Task ProcessLogQueueAsync()
        {
            try
            {
                foreach (var logEntry in _logQueue.GetConsumingEnumerable(_cancellationTokenSource.Token))
                {
                    try
                    {
                        var content = new StringContent(logEntry, Encoding.UTF8, "application/json");
                        // This is a "fire-and-forget" post. We do not await the response to prevent blocking.
                        // Failures are ignored to ensure the primary tool's operation is not affected.
                        _ = _httpClient.PostAsync("", content);
                    }
                    catch
                    {
                        // Suppress any exceptions from the fire-and-forget post
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // This is expected on shutdown
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _logQueue.CompleteAdding();
            _processingTask.Wait(TimeSpan.FromSeconds(2)); // Give it a moment to flush
            _cancellationTokenSource.Dispose();
            _logQueue.Dispose();
        }
    }

    /// <summary>
    /// A logging provider that creates instances of the ExternalHttpLogger.
    /// </summary>
    public class ExternalHttpLoggerProvider : ILoggerProvider
    {
        private readonly AssemblerConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public ExternalHttpLoggerProvider(AssemblerConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ExternalHttpLogger(categoryName, _config, _httpClientFactory);
        }

        public void Dispose() { }
    }
}