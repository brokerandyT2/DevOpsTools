using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Models;

namespace x3squaredcircles.MobileAdapter.Generator.Services
{
    /// <summary>
    /// Defines the contract for a service that performs Git operations.
    /// </summary>
    public interface IGitOperationsService
    {
        Task<bool> IsValidGitRepositoryAsync();
        Task<string> GetCurrentBranchAsync();
        Task<string> GetRepositoryNameAsync();
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

        /// <summary>
        /// Checks if the current working directory is a valid Git repository.
        /// </summary>
        public async Task<bool> IsValidGitRepositoryAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-parse --is-inside-work-tree");
                return result.Success && result.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the current Git branch name, with fallbacks for CI/CD environments.
        /// </summary>
        public async Task<string> GetCurrentBranchAsync()
        {
            try
            {
                // Standard command
                var result = await ExecuteGitCommandAsync("rev-parse --abbrev-ref HEAD");
                if (result.Success && !string.IsNullOrWhiteSpace(result.Output) && !result.Output.Trim().Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return result.Output.Trim();
                }

                // Fallback for detached HEAD states common in CI
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

        /// <summary>
        /// Gets the repository name from the remote origin URL.
        /// </summary>
        public async Task<string> GetRepositoryNameAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("config --get remote.origin.url");
                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    return ExtractRepoNameFromUrl(result.Output.Trim());
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

        private string GetBranchFromCIEnvironment()
        {
            // Azure DevOps
            var azureBranch = Environment.GetEnvironmentVariable("BUILD_SOURCEBRANCHNAME");
            if (!string.IsNullOrEmpty(azureBranch)) return azureBranch;

            // GitHub Actions
            var githubBranch = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
            if (!string.IsNullOrEmpty(githubBranch)) return githubBranch;

            // Jenkins
            var jenkinsBranch = Environment.GetEnvironmentVariable("BRANCH_NAME");
            if (!string.IsNullOrEmpty(jenkinsBranch)) return jenkinsBranch.Replace("origin/", "");

            // GitLab CI
            var gitlabBranch = Environment.GetEnvironmentVariable("CI_COMMIT_REF_NAME");
            return gitlabBranch;
        }

        private string ExtractRepoNameFromUrl(string url)
        {
            try
            {
                var path = new Uri(url).AbsolutePath;
                var lastSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                return lastSegment?.Replace(".git", "", StringComparison.OrdinalIgnoreCase) ?? "unknown";
            }
            catch
            {
                _logger.LogWarning("Could not parse repository name from URL: {Url}", url);
                return "unknown";
            }
        }

        private async Task<(bool Success, string Output, string Error)> ExecuteGitCommandAsync(string arguments)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = _workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _logger.LogDebug("Executing Git command: git {Arguments}", arguments);

            using var process = new Process { StartInfo = processStartInfo };

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
                _logger.LogWarning("Git command failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
                return (false, output, error);
            }

            return (true, output, error);
        }
    }
}