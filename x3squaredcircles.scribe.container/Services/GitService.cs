using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements the logic for interacting with a Git repository by shelling out to the git executable.
    /// </summary>
    public class GitService : IGitService
    {
        private readonly ILogger<GitService> _logger;

        public GitService(ILogger<GitService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<string> GetCommitLogAsync(string workspacePath, string gitRange)
        {
            var gitDirectory = Path.Combine(workspacePath, ".git");
            if (!Directory.Exists(gitDirectory))
            {
                _logger.LogWarning("The specified workspace path '{Path}' does not appear to be a Git repository. Skipping commit log retrieval.", workspacePath);
                return string.Empty;
            }

            // Using --pretty=medium to get a standard, parsable format.
            var arguments = $"log --pretty=medium {gitRange}";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "git", // Assumes 'git' is in the system's PATH
                Arguments = arguments,
                WorkingDirectory = workspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var process = new Process { StartInfo = processStartInfo })
                {
                    _logger.LogDebug("Executing Git command: {FileName} {Arguments}", process.StartInfo.FileName, process.StartInfo.Arguments);

                    process.Start();

                    // Read both streams asynchronously to prevent deadlocks.
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    // Wait for the process to complete. A timeout could be added here for extra resilience.
                    await process.WaitForExitAsync();

                    var errorOutput = await errorTask;
                    if (process.ExitCode != 0)
                    {
                        _logger.LogWarning("Git command exited with a non-zero code ({ExitCode}). Error: {Error}", process.ExitCode, errorOutput);
                        return string.Empty; // Per the contract, return empty on failure.
                    }

                    _logger.LogInformation("Successfully retrieved Git commit log for range '{Range}'.", gitRange);
                    return await outputTask;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A critical error occurred while executing the 'git' process. Please ensure 'git' is installed and accessible in the system's PATH.");
                return string.Empty;
            }
        }
    }
}