using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IGitOperationsService
    {
        // Methods updated to accept the new configuration object
        Task<bool> IsValidGitRepositoryAsync();
        Task ConfigureGitAuthenticationAsync(TokensConfiguration config);
        Task<string?> GetCurrentCommitHashAsync();
        Task<bool> CommitChangesAsync(string message);
        // Add other git methods as needed (tagging, branching, etc.)
        Task CreateTagAsync(string tagName, string message);
        Task PushChangesAsync(string refToPush);
    }

    public class GitOperationsService : IGitOperationsService
    {
        private readonly IAppLogger _logger;
        private readonly string _workingDirectory = "/src";

        public GitOperationsService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<bool> IsValidGitRepositoryAsync()
        {
            var result = await ExecuteGitCommandAsync("rev-parse --is-inside-work-tree");
            return result.ExitCode == 0 && result.Output.Trim() == "true";
        }

        public async Task ConfigureGitAuthenticationAsync(TokensConfiguration config)
        {
            var patToken = Environment.GetEnvironmentVariable("DATALINK_PAT_TOKEN"); // Standardized name
            if (string.IsNullOrEmpty(patToken))
            {
                _logger.LogWarning("No PAT token found in environment. Git push operations will likely fail.");
                return;
            }

            if (config.RepoUrl.StartsWith("https://"))
            {
                var uri = new Uri(config.RepoUrl);
                var authUrl = $"https://x-access-token:{patToken}@{uri.Host}{uri.AbsolutePath}";
                await ExecuteGitCommandAsync($"remote set-url origin \"{authUrl}\"");
                _logger.LogInfo("✓ Git authentication configured using PAT token.");
            }
        }

        public async Task<string?> GetCurrentCommitHashAsync()
        {
            var result = await ExecuteGitCommandAsync("rev-parse HEAD");
            return result.ExitCode == 0 ? result.Output.Trim() : null;
        }

        public async Task<bool> CommitChangesAsync(string message)
        {
            try
            {
                var authorName = Environment.GetEnvironmentVariable("GIT_AUTHOR_NAME") ?? "3SC Design Token Generator";
                var authorEmail = Environment.GetEnvironmentVariable("GIT_AUTHOR_EMAIL") ?? "tools@3sc.com";
                await ExecuteGitCommandAsync($"config user.name \"{authorName}\"");
                await ExecuteGitCommandAsync($"config user.email \"{authorEmail}\"");

                await ExecuteGitCommandAsync("add -A");

                var statusResult = await ExecuteGitCommandAsync("diff --cached --quiet");
                if (statusResult.ExitCode == 0)
                {
                    _logger.LogInfo("No new design token changes detected to commit.");
                    return true;
                }

                var commitResult = await ExecuteGitCommandAsync($"commit -m \"{message}\"");
                if (commitResult.ExitCode != 0)
                {
                    _logger.LogError($"'git commit' command failed: {commitResult.Error}");
                    return false;
                }

                _logger.LogInfo("✓ Changes committed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("An unexpected error occurred during the git commit process.", ex);
                return false;
            }
        }
        // --- NEW: Complete implementation of CreateTagAsync ---
        public async Task CreateTagAsync(string tagName, string message)
        {
            try
            {
                _logger.LogInfo($"Creating git tag: {tagName}");

                var result = await ExecuteGitCommandAsync($"tag -a \"{tagName}\" -m \"{message}\"");
                if (result.ExitCode != 0)
                {
                    // Check if the error is because the tag already exists, which is not a critical failure.
                    if (result.Error.Contains("already exists"))
                    {
                        _logger.LogWarning($"Tag '{tagName}' already exists. Skipping creation.");
                        return;
                    }
                    throw new DesignTokenException(DesignTokenExitCode.GitOperationFailure, $"Failed to create tag '{tagName}': {result.Error}");
                }
                _logger.LogInfo($"✓ Git tag created: {tagName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to create git tag '{tagName}'.", ex);
                if (ex is not DesignTokenException)
                    throw new DesignTokenException(DesignTokenExitCode.GitOperationFailure, $"Failed to create git tag {tagName}: {ex.Message}", ex);
                throw;
            }
        }

        // --- NEW: Complete implementation of PushChangesAsync ---
        public async Task PushChangesAsync(string refToPush)
        {
            try
            {
                _logger.LogInfo($"Pushing ref '{refToPush}' to remote origin...");

                var result = await ExecuteGitCommandAsync($"push origin \"{refToPush}\"");
                if (result.ExitCode != 0)
                {
                    // Check for common non-critical errors, like an up-to-date ref.
                    if (result.Error.Contains("up-to-date"))
                    {
                        _logger.LogInfo($"Ref '{refToPush}' is already up-to-date on remote origin.");
                        return;
                    }
                    throw new DesignTokenException(DesignTokenExitCode.GitOperationFailure, $"Failed to push '{refToPush}': {result.Error}");
                }
                _logger.LogInfo($"✓ Ref '{refToPush}' pushed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to push ref '{refToPush}'.", ex);
                if (ex is not DesignTokenException)
                    throw new DesignTokenException(DesignTokenExitCode.GitOperationFailure, $"Failed to push ref {refToPush}: {ex.Message}", ex);
                throw;
            }
        }

        private async Task<(int ExitCode, string Output, string Error)> ExecuteGitCommandAsync(string arguments)
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

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogDebug($"Git command failed: git {arguments} | Exit: {process.ExitCode} | Stderr: {error.Trim()}");
            }
            else
            {
                _logger.LogDebug($"Git command success: git {arguments}");
            }

            return (process.ExitCode, output.Trim(), error.Trim());
        }
    }
}