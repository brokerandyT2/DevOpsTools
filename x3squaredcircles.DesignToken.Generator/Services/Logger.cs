using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    /// <summary>
    /// A robust, thread-safe logger that handles console output formatting, writes to a
    /// mandatory 'pipeline-tools.log' file, and optionally sends logs to a remote
    /// "firehose" endpoint.
    /// </summary>
    public class Logger : IAppLogger, IDisposable
    {
        private readonly LogLevel _configuredLogLevel;
        private readonly bool _isVerbose;
        private readonly string _logFilePath;
        private readonly HttpClient? _logClient;
        private readonly string? _logEndpointUrl;
        private static readonly object _lockObject = new object();

        public Logger(TokensConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _configuredLogLevel = Enum.TryParse<LogLevel>(config.Logging.LogLevel, true, out var level) ? level : LogLevel.INFO;
            _isVerbose = config.Logging.Verbose;

            var workspacePath = Environment.GetEnvironmentVariable("TOKENS_WORKSPACE") ?? "/src";
            _logFilePath = Path.Combine(workspacePath, "pipeline-tools.log");

            if (!string.IsNullOrEmpty(config.Logging.LogEndpointUrl))
            {
                _logEndpointUrl = config.Logging.LogEndpointUrl;
                _logClient = httpClientFactory.CreateClient("LoggingClient");
                if (!string.IsNullOrEmpty(config.Logging.LogEndpointToken))
                {
                    _logClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.Logging.LogEndpointToken);
                }
            }

            WriteInitialPipelineEntry();
        }

        public void LogDebug(string message) => Log(LogLevel.DEBUG, message);
        public void LogInfo(string message) => Log(LogLevel.INFO, message);
        public void LogWarning(string message) => Log(LogLevel.WARN, message);
        public void LogError(string message, Exception? exception = null) => Log(LogLevel.ERROR, FormatException(message, exception));
        public void LogCritical(string message, Exception? exception = null) => Log(LogLevel.CRITICAL, FormatException(message, exception));
        public void LogStartPhase(string phaseName) => LogInfo($"▶️  Starting Phase: {phaseName}");
        public void LogEndPhase(string phaseName, bool success) => LogInfo($"{(success ? "✅" : "❌")} Finished Phase: {phaseName}");

        private string FormatException(string message, Exception? ex)
        {
            return $"{message}{(ex != null ? $"\n---> Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : "")}";
        }

        private void WriteInitialPipelineEntry()
        {
            try
            {
                var toolName = "3SC-DesignToken-Generator";
                var toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
                var entry = $"{toolName}={toolVersion}";

                lock (_lockObject)
                {
                    var logDirectory = Path.GetDirectoryName(_logFilePath);
                    if (logDirectory != null && !Directory.Exists(logDirectory))
                    {
                        Directory.CreateDirectory(logDirectory);
                    }
                    File.AppendAllText(_logFilePath, entry + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CRITICAL_LOG_FAILURE] Failed to write initial pipeline entry to {_logFilePath}: {ex.Message}");
            }
        }
        private void Log(LogLevel level, string message)
        {
            if (level < _configuredLogLevel || (level == LogLevel.DEBUG && !_isVerbose)) return;

            var timestamp = DateTime.UtcNow;
            var levelString = level.ToString();
            var threadId = Thread.CurrentThread.ManagedThreadId;
            var formattedMessage = $"[{timestamp:o}] [{levelString.PadRight(8)}] [T:{threadId:D3}] {message}";

            lock (_lockObject)
            {
                // Console logging
                ConsoleColor originalColor = Console.ForegroundColor;
                var targetStream = Console.Out;
                switch (level)
                {
                    case LogLevel.DEBUG: Console.ForegroundColor = ConsoleColor.DarkGray; break;
                    case LogLevel.WARN: Console.ForegroundColor = ConsoleColor.Yellow; break;
                    case LogLevel.ERROR:
                    case LogLevel.CRITICAL:
                        Console.ForegroundColor = ConsoleColor.Red;
                        targetStream = Console.Error;
                        break;
                    default: Console.ForegroundColor = ConsoleColor.White; break;
                }
                targetStream.WriteLine(formattedMessage);
                Console.ForegroundColor = originalColor;

                // File logging
                try
                {
                    File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[CRITICAL_LOG_FAILURE] Failed to write to log file {_logFilePath}: {ex.Message}");
                }
            }

            // Firehose logging
            if (_logClient != null)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var logEvent = new
                        {
                            time = timestamp.ToString("o"),
                            host = Environment.MachineName,
                            source = "3SC-DesignToken-Generator",
                            sourcetype = "_json",
                            @event = new { level = levelString, message }
                        };
                        var jsonPayload = JsonSerializer.Serialize(logEvent);
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        await _logClient.PostAsync(_logEndpointUrl, content);
                    }
                    catch (Exception ex)
                    {
                        if (DateTime.UtcNow.Second % 10 == 0)
                        {
                            Console.Error.WriteLine($"[FIREHOSE_LOG_FAILURE]: {ex.Message}");
                        }
                    }
                });
            }
        }

        public void Dispose()
        {
            _logClient?.Dispose();
            GC.SuppressFinalize(this);
        }

        private enum LogLevel
        {
            DEBUG = 0,
            INFO = 1,
            WARN = 2,
            ERROR = 3,
            CRITICAL = 4
        }
    }
}