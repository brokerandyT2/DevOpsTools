using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
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
        void LogStructuredEvent(string eventType, object data);
    }

    /// <summary>
    /// A robust, thread-safe logger that handles console output formatting and
    /// writes to the mandatory 'pipeline-tools.log' file.
    /// </summary>
    public class Logger : IAppLogger
    {
        private readonly LogLevel _configuredLogLevel;
        private readonly bool _isVerbose;
        private readonly string _logFilePath;
        private static readonly object _lockObject = new object();

        // This constructor now correctly receives the configuration object directly from the DI container.
        public Logger(DataLinkConfiguration config)
        {
            _configuredLogLevel = Enum.TryParse<LogLevel>(config.LogLevel, true, out var level) ? level : LogLevel.INFO;
            _isVerbose = config.Verbose;

            var workspacePath = "/src";
            _logFilePath = Path.Combine(workspacePath, "pipeline-tools.log");

            WriteInitialPipelineEntry();
        }

        public void LogDebug(string message) => Log(LogLevel.DEBUG, message);
        public void LogInfo(string message) => Log(LogLevel.INFO, message);
        public void LogWarning(string message) => Log(LogLevel.WARN, message);
        public void LogError(string message, Exception? exception = null) => Log(LogLevel.ERROR, $"{message}{(exception != null ? $"\n---> Exception: {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}" : "")}");
        public void LogCritical(string message, Exception? exception = null) => Log(LogLevel.CRITICAL, $"{message}{(exception != null ? $"\n---> Exception: {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}" : "")}");

        public void LogConfiguration(DataLinkConfiguration config)
        {
            LogInfo("--- DataLink Configuration ---");
            LogInfo($"  Source Repository: {config.BusinessLogicRepo}");
            LogInfo($"  Test Harness Repository: {config.TestHarnessRepo ?? "Not Provided"}");
            LogInfo($"  Destination Repository: {config.DestinationRepo}");
            LogInfo($"  Version Tag Pattern: {config.VersionTagPattern}");
            LogInfo($"  Generate Test Harness: {config.GenerateTestHarness}");
            if (config.ContinueOnTestFailure)
            {
                LogWarning(">> Test Failure Override: Continue on test failure is ENABLED.");
            }
            LogInfo("------------------------------");
        }

        public void LogStartPhase(string phaseName) => LogInfo($"▶️  Starting Phase: {phaseName}");
        public void LogEndPhase(string phaseName, bool success)
        {
            var status = success ? "✅" : "❌";
            LogInfo($"{status} Finished Phase: {phaseName}");
        }

        public void LogStructuredEvent(string eventType, object data)
        {
            var logEntry = new { Timestamp = DateTime.UtcNow, Event = eventType, Data = data };
            var json = JsonSerializer.Serialize(logEntry);
            Log(LogLevel.INFO, $"[STRUCTURED_EVENT] {json}");
        }

        private void WriteInitialPipelineEntry()
        {
            try
            {
                var toolName = "3SC-DataLink";
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
                var errorMessage = $"[CRITICAL_LOG_FAILURE] Failed to write initial pipeline entry to {_logFilePath}: {ex.Message}";
                Console.Error.WriteLine(errorMessage);
            }
        }

        private void Log(LogLevel level, string message)
        {
            if (level < _configuredLogLevel) return;
            if (level == LogLevel.DEBUG && !_isVerbose) return;

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffZ");
            var levelString = level.ToString().PadRight(8);
            var threadId = Thread.CurrentThread.ManagedThreadId;
            var formattedMessage = $"[{timestamp}] [{levelString}] [T:{threadId:D3}] {message}";

            lock (_lockObject)
            {
                ConsoleColor originalColor = Console.ForegroundColor;
                var targetStream = Console.Out;

                switch (level)
                {
                    case LogLevel.DEBUG:
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        break;
                    case LogLevel.WARN:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case LogLevel.ERROR:
                    case LogLevel.CRITICAL:
                        Console.ForegroundColor = ConsoleColor.Red;
                        targetStream = Console.Error;
                        break;
                    default: // INFO
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                }
                targetStream.WriteLine(formattedMessage);
                Console.ForegroundColor = originalColor;

                try
                {
                    File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    var errorMessage = $"[CRITICAL_LOG_FAILURE] Failed to write to log file {_logFilePath}: {ex.Message}";
                    Console.Error.WriteLine(errorMessage);
                }
            }
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