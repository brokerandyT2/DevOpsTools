using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IPipelineLoggingService
    {
        Task WriteToolVersionAsync(string toolName, string version);
        Task WriteLogEntryAsync(string entry);
        Task WriteLogEntryAsync(string key, string value);
    }

    public class PipelineLoggingService : IPipelineLoggingService
    {
        private readonly ILogger<PipelineLoggingService> _logger;
        private readonly string _outputDirectory = "/src";
        private readonly string _logFileName = "pipeline-tools.log";

        public PipelineLoggingService(ILogger<PipelineLoggingService> logger)
        {
            _logger = logger;
        }

        public async Task WriteToolVersionAsync(string toolName, string version)
        {
            var entry = $"{toolName}={version}";
            await WriteLogEntryAsync(entry);
        }

        public async Task WriteLogEntryAsync(string entry)
        {
            try
            {
                var logFilePath = Path.Combine(_outputDirectory, _logFileName);
                await File.AppendAllTextAsync(logFilePath, entry + Environment.NewLine);
                _logger.LogDebug("Pipeline log entry written: {Entry}", entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write pipeline log entry: {Entry}", entry);
            }
        }

        public async Task WriteLogEntryAsync(string key, string value)
        {
            await WriteLogEntryAsync($"{key}={value}");
        }
    }
}