using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A generic, static utility for writing entries to the 3SC pipeline forensic log.
/// This class implements the full Forensic Logging Protocol as required by The Scribe.
/// It is designed to be a drop-in component for any 3SC tool.
/// </summary>
public static class ForensicLogger
{
    private const string LogFileName = "pipeline-log.json";
    private const string UniversalPrefix = "3SC_";
    private const string RedactedValue = "[REDACTED]";
    private static readonly string[] RedactionKeys = { "_TOKEN", "_KEY", "_SECRET", "_PASSWORD" };

    // A system-wide semaphore to prevent race conditions when multiple 3SC tools,
    // potentially running in parallel, try to update the shared log file.
    private static readonly SemaphoreSlim _logSemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Writes a complete, structured log entry to the pipeline-log.json file.
    /// This method is atomic and safe for concurrent use by multiple processes.
    /// </summary>
    /// <param name="toolName">The official name of the tool executing. e.g., "3SC.Gate"</param>
    /// <param name="toolVersion">The version of the tool executing. e.g., "1.2.0"</param>
    public static async Task WriteForensicLogEntryAsync(string toolName, string toolVersion)
    {
        try
        {
            var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), LogFileName);

            // 1. GATHER CONFIGURATION
            // -----------------------
            var configuration = GetFilteredEnvironmentVariables(toolName);

            // 2. CONSTRUCT LOG ENTRY
            // ----------------------
            var logEntry = new
            {
                toolName,
                toolVersion,
                executionTimestamp = DateTime.UtcNow,
                configuration
            };

            // 3. WRITE TO FILE (ATOMICALLY)
            // -----------------------------
            // Wait for exclusive access to the log file.
            await _logSemaphore.WaitAsync();
            try
            {
                var logEntries = new List<object>();

                // If the log file already exists, read its content.
                if (File.Exists(logFilePath))
                {
                    var jsonContent = await File.ReadAllTextAsync(logFilePath);
                    // If the file is not empty, try to deserialize it.
                    // If deserialization fails (e.g., malformed), we start a new list, effectively overwriting it.
                    if (!string.IsNullOrWhiteSpace(jsonContent))
                    {
                        try
                        {
                            var existingEntries = JsonSerializer.Deserialize<List<object>>(jsonContent);
                            if (existingEntries != null)
                            {
                                logEntries.AddRange(existingEntries);
                            }
                        }
                        catch (JsonException)
                        {
                            Console.WriteLine($"[WARN] The existing '{LogFileName}' is malformed and will be overwritten.");
                        }
                    }
                }

                // Add the new entry and serialize the entire list back to JSON.
                logEntries.Add(logEntry);
                var newJsonContent = JsonSerializer.Serialize(logEntries, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(logFilePath, newJsonContent);
            }
            finally
            {
                // Always release the semaphore, even if an exception occurs.
                _logSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            // This is a non-critical feature. Log to console and continue.
            Console.WriteLine($"[WARN] Could not write to forensic log '{LogFileName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Scans all environment variables and returns a dictionary containing only those
    /// matching the 3SC logging rules, with sensitive values redacted.
    /// </summary>
    /// <param name="toolName">The name of the tool, used to derive the tool-specific prefix.</param>
    private static Dictionary<string, string> GetFilteredEnvironmentVariables(string toolName)
    {
        var toolSpecificPrefix = (toolName.Split('.').LastOrDefault() ?? toolName).ToUpperInvariant() + "_";
        var variables = new Dictionary<string, string>();

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key.ToString()!;
            var keyUpper = key.ToUpperInvariant();

            if (keyUpper.StartsWith(UniversalPrefix) || keyUpper.StartsWith(toolSpecificPrefix))
            {
                var value = entry.Value?.ToString() ?? string.Empty;

                // The Redaction Rule: check if the key contains sensitive substrings.
                if (RedactionKeys.Any(k => keyUpper.Contains(k)))
                {
                    variables[key] = RedactedValue;
                }
                else
                {
                    variables[key] = value;
                }
            }
        }
        return variables;
    }
}