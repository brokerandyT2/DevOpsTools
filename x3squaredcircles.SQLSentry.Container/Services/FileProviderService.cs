using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.SQLSentry.Container.Models;

namespace x3squaredcircles.SQLSentry.Container.Services
{
    /// <summary>
    /// Defines the contract for a service that provides file content from the local file system.
    /// </summary>
    public interface IFileProviderService
    {
        /// <summary>
        /// Reads the content of the consolidated SQL file.
        /// </summary>
        /// <param name="filePath">The absolute path to the SQL file.</param>
        /// <returns>The string content of the SQL file.</returns>
        Task<string> GetSqlFileContentAsync(string filePath);

        /// <summary>
        /// Reads and deserializes the exception file, if it exists.
        /// </summary>
        /// <param name="filePath">The absolute path to the exceptions JSON file.</param>
        /// <returns>An ExceptionFile object or null if the file does not exist.</returns>
        Task<ExceptionFile?> GetExceptionsAsync(string? filePath);

        /// <summary>
        /// Reads and deserializes the custom patterns file, if it exists.
        /// </summary>
        /// <param name="filePath">The absolute path to the patterns JSON file.</param>
        /// <returns>A PatternFile object or null if the file does not exist.</returns>
        Task<PatternFile?> GetPatternsAsync(string? filePath);
    }

    /// <summary>
    /// Implements file reading and deserialization from the local container file system.
    /// </summary>
    public class FileProviderService : IFileProviderService
    {
        private readonly ILogger<FileProviderService> _logger;

        public FileProviderService(ILogger<FileProviderService> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<string> GetSqlFileContentAsync(string filePath)
        {
            _logger.LogInformation("Reading consolidated SQL file from: {FilePath}", filePath);
            return await ReadFileContentAsync(filePath, isRequired: true);
        }

        /// <inheritdoc />
        public async Task<ExceptionFile?> GetExceptionsAsync(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogInformation("No exceptions file path provided. Proceeding without suppressions.");
                return null;
            }

            _logger.LogInformation("Reading exceptions file from: {FilePath}", filePath);
            var content = await ReadFileContentAsync(filePath, isRequired: false);

            if (string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Exceptions file at '{FilePath}' was not found or is empty.", filePath);
                return null;
            }

            try
            {
                var exceptionFile = JsonSerializer.Deserialize<ExceptionFile>(content);
                _logger.LogInformation("✓ Successfully parsed exceptions file. Found {Count} suppression rules.", exceptionFile?.Suppressions.Count ?? 0);
                return exceptionFile;
            }
            catch (JsonException ex)
            {
                throw new GuardianException(ExitCode.FileReadFailed, "JSON_PARSE_ERROR", $"Failed to parse exceptions file '{filePath}'. Invalid JSON: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public async Task<PatternFile?> GetPatternsAsync(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogInformation("No custom patterns file path provided. Using built-in patterns only.");
                return null;
            }

            _logger.LogInformation("Reading custom patterns file from: {FilePath}", filePath);
            var content = await ReadFileContentAsync(filePath, isRequired: false);

            if (string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Custom patterns file at '{FilePath}' was not found or is empty.", filePath);
                return null;
            }

            try
            {
                var patternFile = JsonSerializer.Deserialize<PatternFile>(content);
                _logger.LogInformation("✓ Successfully parsed custom patterns file. Found {Count} patterns.", patternFile?.Patterns.Count ?? 0);
                return patternFile;
            }
            catch (JsonException ex)
            {
                throw new GuardianException(ExitCode.FileReadFailed, "JSON_PARSE_ERROR", $"Failed to parse patterns file '{filePath}'. Invalid JSON: {ex.Message}", ex);
            }
        }

        private async Task<string> ReadFileContentAsync(string filePath, bool isRequired)
        {
            if (!File.Exists(filePath))
            {
                if (isRequired)
                {
                    _logger.LogError("Required file not found at path: {FilePath}", filePath);
                    throw new GuardianException(ExitCode.FileReadFailed, "REQUIRED_FILE_NOT_FOUND", $"The required file could not be found at the specified path: {filePath}");
                }
                return string.Empty;
            }

            try
            {
                return await File.ReadAllTextAsync(filePath);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "An IO error occurred while reading file: {FilePath}", filePath);
                throw new GuardianException(ExitCode.FileReadFailed, "FILE_IO_ERROR", $"An error occurred reading the file: {filePath}", ex);
            }
        }
    }
}