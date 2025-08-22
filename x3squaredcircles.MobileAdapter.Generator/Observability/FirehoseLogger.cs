using Microsoft.Extensions.Logging;
using System;
using System.Reflection;

namespace x3squaredcircles.MobileAdapter.Generator.Observability
{
    /// <summary>
    /// An ILogger implementation that formats log messages into a standardized
    /// FirehoseLogMessage and sends them to the FirehoseLoggerProvider for batching and dispatch.
    /// </summary>
    public class FirehoseLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FirehoseLoggerProvider _provider;
        private static readonly string _serviceName = Assembly.GetExecutingAssembly().GetName().Name ?? "mobile-adapter-generator";
        private static readonly string _serviceVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public FirehoseLogger(string categoryName, FirehoseLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            // Scopes are not supported by this simple logger implementation.
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            // The logger is always enabled if the provider is configured.
            // Filtering can be added here if needed in the future.
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var logMessage = FirehoseLogMessage.FromLogEntry(
                _serviceName,
                _serviceVersion,
                _categoryName,
                logLevel,
                eventId,
                state,
                exception,
                formatter
            );

            _provider.PostMessage(logMessage);
        }
    }
}