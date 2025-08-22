using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Models.Forensic;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements the logic for reading and parsing the forensic log of record, 'pipeline-log.json'.
    /// </summary>
    public class ForensicLogService : IForensicLogService
    {
        private const string LogFileName = "pipeline-log.json";
        private readonly ILogger<ForensicLogService> _logger;
        private List<LogEntry> _logEntries = new List<LogEntry>();

        /// <inheritdoc />
        public IEnumerable<LogEntry> Entries => _logEntries;

        /// <summary>
        /// Initializes a new instance of the <see cref="ForensicLogService"/> class.
        /// </summary>
        /// <param name="logger">The logger for forensic and operational messages.</param>
        public ForensicLogService(ILogger<ForensicLogService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task LoadLogEntriesAsync(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
            {
                throw new DirectoryNotFoundException($"The provided workspace path '{workspacePath}' is invalid or does not exist.");
            }

            var logFilePath = Path.Combine(workspacePath, LogFileName);
            _logger.LogInformation("Attempting to load forensic log from: {Path}", logFilePath);

            if (!File.Exists(logFilePath))
            {
                _logger.LogWarning("The forensic log file '{FileName}' was not found. Tool configuration data will be unavailable.", LogFileName);
                _logEntries = new List<LogEntry>(); // Ensure the list is empty and return gracefully.
                return;
            }

            try
            {
                var jsonContent = await File.ReadAllTextAsync(logFilePath);

                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    _logger.LogWarning("The forensic log file '{FileName}' was found but is empty.", LogFileName);
                    _logEntries = new List<LogEntry>();
                    return;
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };

                var entries = JsonSerializer.Deserialize<List<LogEntry>>(jsonContent, options);

                if (entries == null)
                {
                    throw new InvalidDataException($"Deserialization of '{LogFileName}' resulted in a null object. The file may be malformed.");
                }

                _logEntries = entries;
                _logger.LogInformation("Successfully loaded {Count} entries from the forensic log.", _logEntries.Count);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogCritical(jsonEx, "Failed to parse the forensic log file at {Path}. The file is malformed.", logFilePath);
                throw new InvalidDataException($"The forensic log file '{LogFileName}' is not valid JSON. See inner exception for details.", jsonEx);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "A critical error occurred while reading the forensic log file at {Path}.", logFilePath);
                throw new IOException($"Failed to read the forensic log file at '{logFilePath}'. See inner exception for details.", ex);
            }
        }
    }
}