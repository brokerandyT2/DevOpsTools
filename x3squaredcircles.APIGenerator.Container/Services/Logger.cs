using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Defines a standardized logging contract for the application.
    /// </summary>
    public interface IAppLogger
    {
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
        void LogCritical(string message, Exception? exception = null);
        void LogConfiguration(DataLinkConfiguration config);
        void LogStartPhase(string phaseName);
        void LogEndPhase(string phaseName, bool success);
    }

    /// <summary>
    /// A robust, thread-safe logger that handles console output formatting, writes to a
    /// mandatory 'pipeline-tools.log' file, and optionally sends logs to a remote
    /// "firehose" endpoint (e.g., Splunk HEC).
    /// </summary>
    public class Logger : IAppLogger, IDisposable
    {
        private readonly LogLevel _configuredLogLevel;
        private readonly bool _isVerbose;
        private readonly string _logFilePath;
        private readonly HttpClient? _logClient;
        private readonly string? _logEndpointUrl;
        private static readonly object _lockObject = new object();

        public Logger(DataLinkConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _configuredLogLevel = Enum.TryParse<LogLevel>(config.LogLevel, true, out var level) ? level : LogLevel.INFO;
            _isVerbose = config.Verbose;

            var workspacePath = Environment.GetEnvironmentVariable("DATALINK_WORKSPACE") ?? "/src";
            _logFilePath = Path.Combine(workspacePath, "pipeline-tools.log");

            // Configure the HTTP client for the firehose endpoint, if provided.
            if (!string.IsNullOrEmpty(config.LogEndpointUrl))
            {
                _logEndpointUrl = config.LogEndpointUrl;
                _logClient = httpClientFactory.CreateClient("LoggingClient");
                // The Composite Endpoint format is `https://endpoint.com=$(TokenName)`
                if (_logEndpointUrl.Contains("=$(") && !string.IsNullOrEmpty(config.LogEndpointToken))
                {
                    var tokenName = _logEndpointUrl.Split(new[] { "=$(" }, StringSplitOptions.None)[1].TrimEnd(')');
                    _logEndpointUrl = _logEndpointUrl.Split('=')[0];
                    // Example for Splunk HEC. This could be made more generic.
                    _logClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Splunk", config.LogEndpointToken);
                }
                else if (!string.IsNullOrEmpty(config.LogEndpointToken))
                {
                    _logClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.LogEndpointToken);
                }
            }

            WriteInitialPipelineEntry();
        }

        public void LogDebug(string message) => Log(LogLevel.DEBUG, message);
        public void LogInfo(string message) => Log(LogLevel.INFO, message);
        public void LogWarning(string message) => Log(LogLevel.WARN, message);
        public void LogError(string message, Exception? exception = null) => Log(LogLevel.ERROR, FormatException(message, exception));
        public void LogCritical(string message, Exception? exception = null) => Log(LogLevel.CRITICAL, FormatException(message, exception));

        public void LogConfiguration(DataLinkConfiguration config)
        {
            LogInfo("--- DataLink Configuration ---");
            LogInfo($"  Source Repository: {config.BusinessLogicRepo}");
            LogInfo($"  Test Harness Repository: {config.TestHarnessRepo ?? "Not Provided"}");
            LogInfo($"  Destination Repository: {config.DestinationRepo}");
            LogInfo($"  Version Tag Pattern: {config.VersionTagPattern}");
            LogInfo($"  Generate Test Harness: {config.GenerateTestHarness}");
            LogInfo($"  Target Language: {config.TargetLanguage}");
            LogInfo($"  Cloud Provider: {config.CloudProvider}");
            LogInfo($"  Log Endpoint: {_logEndpointUrl ?? "Not Configured"}");
            if (config.ContinueOnTestFailure)
            {
                LogWarning(">> Test Failure Override: Continue on test failure is ENABLED.");
            }
            LogInfo("------------------------------");
        }

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
                var toolName = "3SC-DataLink";
                var toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "6.0.0";
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
                    // If file logging fails, we can't do much but report it to the console.
                    Console.Error.WriteLine($"[CRITICAL_LOG_FAILURE] Failed to write to log file {_logFilePath}: {ex.Message}");
                }
            }

            // Firehose logging (outside the lock for performance)
            if (_logClient != null)
            {
                // Use Task.Run to fire-and-forget without blocking the main thread's logging performance.
                // This is a trade-off: we prioritize application speed over guaranteed log delivery.
                Task.Run(async () =>
                {
                    try
                    {
                        // Some logging platforms (like Splunk HEC) prefer a specific JSON structure.
                        var logEvent = new
                        {
                            time = timestamp.ToString("o"),
                            host = Environment.MachineName,
                            source = "3SC-DataLink-Assembler",
                            sourcetype = "_json",
                            @event = new { level = levelString, message }
                        };
                        var jsonPayload = JsonSerializer.Serialize(logEvent);
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        // We don't await this or check the result in a fire-and-forget scenario.
                        await _logClient.PostAsync(_logEndpointUrl, content);
                    }
                    catch (Exception ex)
                    {
                        // We can't log a logging failure to the firehose, so just write to console error.
                        // We write this infrequently to avoid spamming the console on network outages.
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