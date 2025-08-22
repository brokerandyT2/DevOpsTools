using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// A background service that sends structured log events to one or more configured external HTTP endpoints.
    /// This provides a "firehose" of operational logs for enterprise observability platforms like Splunk or Datadog.
    /// It is designed to be "fire-and-forget" to never impact the primary tool's execution.
    /// </summary>
    public class ExternalHttpLogger : ILogger, IDisposable
    {
        private readonly string _categoryName;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly List<(Uri Endpoint, string? Token)> _endpoints;
        private readonly Dictionary<string, string> _staticMetadata;
        private readonly BlockingCollection<string> _logQueue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private readonly Task _processingTask;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private static readonly string _toolName = Assembly.GetExecutingAssembly().GetName().Name ?? "3sc-api-assembler";
        private static readonly string _toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public ExternalHttpLogger(string categoryName, AssemblerConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _categoryName = categoryName;
            _httpClientFactory = httpClientFactory;
            _endpoints = ParseCompositeEndpoints(config);
            _staticMetadata = ParseStaticMetadata();

            // Only start the processing task if there are valid endpoints to log to.
            if (_endpoints.Any())
            {
                _processingTask = Task.Run(ProcessLogQueueAsync, _cancellationTokenSource.Token);
            }
        }

        public IDisposable BeginScope<TState>(TState state) => default!;

        public bool IsEnabled(LogLevel logLevel) => _endpoints.Any() && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var logEntry = FormatLogEntry(logLevel, eventId, state, exception, formatter);
            // Do not block the logging thread. If the queue is full, we drop the log.
            _logQueue.TryAdd(logEntry);
        }

        private string FormatLogEntry<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var logPayload = new Dictionary<string, object>
            {
                { "timestamp", DateTime.UtcNow },
                { "level", logLevel.ToString() },
                { "toolName", _toolName },
                { "toolVersion", _toolVersion },
                { "category", _categoryName },
                { "eventId", eventId.Id },
                { "message", formatter(state, exception) }
            };

            if (exception != null)
            {
                logPayload["exception"] = exception.ToString();
            }

            foreach (var meta in _staticMetadata)
            {
                logPayload[meta.Key] = meta.Value;
            }

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            return JsonSerializer.Serialize(logPayload, jsonOptions);
        }

        private async Task ProcessLogQueueAsync()
        {
            try
            {
                foreach (var logEntryJson in _logQueue.GetConsumingEnumerable(_cancellationTokenSource.Token))
                {
                    var content = new StringContent(logEntryJson, Encoding.UTF8, "application/json");

                    // Multicast the log to all configured endpoints.
                    foreach (var (endpoint, token) in _endpoints)
                    {
                        try
                        {
                            using var client = _httpClientFactory.CreateClient("ExternalLogger");
                            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
                            if (!string.IsNullOrEmpty(token))
                            {
                                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                            }

                            // Fire-and-forget post. We do not await to prevent blocking.
                            // Failures are ignored to ensure the primary tool's operation is not affected.
                            _ = client.SendAsync(request, _cancellationTokenSource.Token);
                        }
                        catch
                        {
                            // Suppress any exceptions from the fire-and-forget post.
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // This is expected on shutdown.
            }
        }

        private static List<(Uri, string?)> ParseCompositeEndpoints(AssemblerConfiguration config)
        {
            var endpoints = new List<(Uri, string?)>();
            var endpointStr = config.Logging.ExternalLogEndpoint;
            if (string.IsNullOrWhiteSpace(endpointStr)) return endpoints;

            var definitions = endpointStr.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var def in definitions)
            {
                var resolvedDef = Environment.ExpandEnvironmentVariables(def.Replace("$(", "%").Replace(")", "%"));
                var parts = resolvedDef.Split('=', 2);

                if (Uri.TryCreate(parts[0], UriKind.Absolute, out var uri))
                {
                    string? token = null;
                    if (parts.Length > 1)
                    {
                        token = Environment.GetEnvironmentVariable(parts[1]);
                    }
                    else
                    {
                        // Fallback to the default token if no inline token is specified.
                        token = config.Logging.ExternalLogToken;
                    }
                    endpoints.Add((uri, token));
                }
            }
            return endpoints;
        }

        private static Dictionary<string, string> ParseStaticMetadata()
        {
            var metadata = new Dictionary<string, string>();
            var metadataStr = Environment.GetEnvironmentVariable("3SC_LOG_STATIC_METADATA");
            if (string.IsNullOrWhiteSpace(metadataStr)) return metadata;

            var pairs = metadataStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    metadata[parts[0].Trim()] = Environment.ExpandEnvironmentVariables(parts[1].Trim().Replace("$(", "%").Replace(")", "%"));
                }
            }
            return metadata;
        }

        public void Dispose()
        {
            // Give the queue a very brief moment to flush on shutdown.
            _logQueue.CompleteAdding();
            try
            {
                _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(2));
                _processingTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch (OperationCanceledException) { }
            catch (AggregateException) { } // Ignore exceptions on shutdown
            finally
            {
                _cancellationTokenSource.Dispose();
                _logQueue.Dispose();
            }
        }
    }

    /// <summary>
    /// A logging provider that creates instances of the ExternalHttpLogger.
    /// This is registered in Program.cs to enable the external logging feature.
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
            // Only create a logger if the configuration is valid.
            if (string.IsNullOrWhiteSpace(_config.Logging.ExternalLogEndpoint))
            {
                return new NullLogger(); // Return a no-op logger if not configured
            }
            return new ExternalHttpLogger(categoryName, _config, _httpClientFactory);
        }

        public void Dispose() { }

        // A simple ILogger that does nothing, for when the provider is not configured.
        private sealed class NullLogger : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) => default!;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }
}