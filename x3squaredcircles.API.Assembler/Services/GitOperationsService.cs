using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for a service that performs Git operations.
    /// </summary>
    public interface IGitOperationsService
    {
        Task<string> GetCurrentBranchAsync();
        Task<string> GetRepositoryNameAsync();
        Task<string> GetCommitHashAsync(bool full = false);
    }

    /// <summary>
    /// Provides functionality for interacting with a Git repository from the command line.
    /// It sources core context (repo, branch) from the central configuration and executes commands for runtime data.
    /// </summary>
    public class GitOperationsService : IGitOperationsService
    {
        private readonly ILogger<GitOperationsService> _logger;
        private readonly AssemblerConfiguration _config;
        private readonly string _workingDirectory = "/src"; // Assumes running in a container with a mounted volume

        public GitOperationsService(ILogger<GitOperationsService> logger, AssemblerConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// Returns the current branch name directly from the validated configuration.
        /// </summary>
        public Task<string> GetCurrentBranchAsync()
        {
            // The branch is now sourced from the 3SC_BRANCH or ASSEMBLER_BRANCH variable.
            // No need for complex CI variable sniffing.
            if (string.IsNullOrWhiteSpace(_config.Branch))
            {
                _logger.LogWarning("Branch name is not configured. Falling back to 'unknown'.");
                return Task.FromResult("unknown");
            }
            _logger.LogDebug("Resolved branch name from configuration: {BranchName}", _config.Branch);
            return Task.FromResult(_config.Branch);
        }

        /// <summary>
        /// Returns the repository name derived from the configured RepoUrl.
        /// </summary>
        public Task<string> GetRepositoryNameAsync()
        {
            // The repo URL is now sourced from the 3SC_REPO_URL or ASSEMBLER_REPO_URL variable.
            if (string.IsNullOrWhiteSpace(_config.RepoUrl))
            {
                _logger.LogWarning("Repository URL is not configured. Falling back to 'unknown-repo'.");
                return Task.FromResult("unknown-repo");
            }

            try
            {
                var repoName = _config.RepoUrl.Split('/').LastOrDefault()?.Replace(".git", "", StringComparison.OrdinalIgnoreCase);
                var result = repoName ?? "unknown-repo";
                _logger.LogDebug("Resolved repository name '{RepoName}' from URL '{RepoUrl}'", result, _config.RepoUrl);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse repository name from configured URL '{RepoUrl}'. Falling back to 'unknown-repo'.", _config.RepoUrl);
                return Task.FromResult("unknown-repo");
            }
        }

        /// <summary>
        /// Executes a git command to get the current commit hash at runtime.
        /// </summary>
        public async Task<string> GetCommitHashAsync(bool full = false)
        {
            try
            {
                var args = full ? "rev-parse HEAD" : "rev-parse --short HEAD";
                var (success, output, error) = await ExecuteGitCommandAsync(args);

                if (success && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }

                _logger.LogWarning("Git command to get commit hash failed. Error: {Error}", error);
                return "unknown";

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get commit hash, using fallback 'unknown'.");
                return "unknown";
            }
        }

        private async Task<(bool Success, string Output, string Error)> ExecuteGitCommandAsync(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = _workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }

            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            // Use TaskCompletionSource for more robust async process handling
            var processExitCompletionSource = new TaskCompletionSource<int>();
            process.Exited += (sender, args) => processExitCompletionSource.SetResult(process.ExitCode);
            process.EnableRaisingEvents = true;

            process.OutputDataReceived += (_, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); };
            process.ErrorDataReceived += (_, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var exitCode = await processExitCompletionSource.Task;
            var output = outputBuilder.ToString().Trim();
            var error = errorBuilder.ToString().Trim();

            if (exitCode != 0)
            {
                _logger.LogDebug("Git command failed: git {Args}. Exit Code: {Code}. Error: {Error}", arguments, exitCode, error);
                return (false, output, error);
            }

            return (true, output, error);
        }
    }
}