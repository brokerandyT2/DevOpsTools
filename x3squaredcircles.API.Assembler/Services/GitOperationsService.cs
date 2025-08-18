using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    /// Used to gather context for forensic logging and tag generation.
    /// </summary>
    public class GitOperationsService : IGitOperationsService
    {
        private readonly ILogger<GitOperationsService> _logger;
        private readonly string _workingDirectory = "/src"; // Assumes running in a container with a mounted volume

        public GitOperationsService(ILogger<GitOperationsService> logger)
        {
            _logger = logger;
        }

        public async Task<string> GetCurrentBranchAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-parse --abbrev-ref HEAD");
                if (result.Success && !string.IsNullOrWhiteSpace(result.Output) && !result.Output.Trim().Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return result.Output.Trim();
                }

                var ciBranch = GetBranchFromCIEnvironment();
                if (!string.IsNullOrEmpty(ciBranch))
                {
                    _logger.LogDebug("Resolved branch name from CI environment variable: {BranchName}", ciBranch);
                    return ciBranch;
                }

                _logger.LogWarning("Could not determine current branch name. Falling back to 'unknown'.");
                return "unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get current branch name, using fallback 'unknown'.");
                return "unknown";
            }
        }

        public async Task<string> GetRepositoryNameAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("config --get remote.origin.url");
                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    var url = result.Output.Trim();
                    var repoName = url.Split('/').LastOrDefault()?.Replace(".git", "", StringComparison.OrdinalIgnoreCase);
                    return repoName ?? "unknown-repo";
                }

                _logger.LogWarning("Could not determine Git remote URL. Falling back to directory name.");
                return new System.IO.DirectoryInfo(_workingDirectory).Name;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get repository name, using fallback 'unknown-repo'.");
                return "unknown-repo";
            }
        }

        public async Task<string> GetCommitHashAsync(bool full = false)
        {
            try
            {
                var args = full ? "rev-parse HEAD" : "rev-parse --short HEAD";
                var result = await ExecuteGitCommandAsync(args);
                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    return result.Output.Trim();
                }
                return "unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get commit hash, using fallback 'unknown'.");
                return "unknown";
            }
        }

        private string GetBranchFromCIEnvironment()
        {
            return Environment.GetEnvironmentVariable("BUILD_SOURCEBRANCHNAME")      // Azure DevOps
                ?? Environment.GetEnvironmentVariable("GITHUB_REF_NAME")             // GitHub Actions
                ?? Environment.GetEnvironmentVariable("BRANCH_NAME")                 // Jenkins
                ?? Environment.GetEnvironmentVariable("CI_COMMIT_REF_NAME");         // GitLab CI
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

            process.OutputDataReceived += (_, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); };
            process.ErrorDataReceived += (_, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var output = outputBuilder.ToString().Trim();
            var error = errorBuilder.ToString().Trim();

            if (process.ExitCode != 0)
            {
                _logger.LogDebug("Git command failed: git {Args}. Exit Code: {Code}. Error: {Error}", arguments, process.ExitCode, error);
                return (false, output, error);
            }

            return (true, output, error);
        }
    }
}