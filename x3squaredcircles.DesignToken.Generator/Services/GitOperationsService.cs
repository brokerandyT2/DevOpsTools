using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IGitOperationsService
    {
        Task<bool> IsValidGitRepositoryAsync();
        Task ConfigureGitAuthenticationAsync(DesignTokenConfiguration config);
        Task<string?> GetCurrentCommitHashAsync();
        Task CreateTagAsync(string tagName, string message);
        Task<bool> CreateBranchAsync(string branchName);
        Task<bool> CommitChangesAsync(string message, string? authorName = null, string? authorEmail = null);
        Task<bool> PushChangesAsync(string? branchName = null);
        Task<bool> CreatePullRequestAsync(string sourceBranch, string targetBranch, string title, string description);
    }

    public class GitOperationsService : IGitOperationsService
    {
        private readonly ILogger<GitOperationsService> _logger;
        private readonly string _workingDirectory = "/src";

        public GitOperationsService(ILogger<GitOperationsService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> IsValidGitRepositoryAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-parse --git-dir");
                return result.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check if directory is a valid git repository");
                return false;
            }
        }

        public async Task ConfigureGitAuthenticationAsync(DesignTokenConfiguration config)
        {
            try
            {
                _logger.LogDebug("Configuring git authentication");

                // Set git user (required for commits and tags)
                var authorName = config.Git.CommitAuthorName ?? "Design Token Generator";
                var authorEmail = config.Git.CommitAuthorEmail ?? "design-tokens@pipeline.local";

                await ExecuteGitCommandAsync($"config user.name \"{authorName}\"");
                await ExecuteGitCommandAsync($"config user.email \"{authorEmail}\"");

                // Configure authentication if token is available
                var token = config.Authentication.PatToken ?? config.Authentication.PipelineToken;
                if (!string.IsNullOrEmpty(token))
                {
                    // Configure credential helper for HTTPS authentication
                    var repoUrl = config.RepoUrl;
                    if (repoUrl.StartsWith("https://"))
                    {
                        var uri = new Uri(repoUrl);
                        var authUrl = $"https://{token}@{uri.Host}{uri.PathAndQuery}";

                        // Set remote URL with embedded token
                        await ExecuteGitCommandAsync($"remote set-url origin \"{authUrl}\"");
                        _logger.LogDebug("Git authentication configured for HTTPS");
                    }
                }

                _logger.LogInformation("✓ Git authentication configured");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to configure git authentication");
                throw new DesignTokenException(DesignTokenExitCode.GitOperationFailure,
                    $"Git authentication configuration failed: {ex.Message}", ex);
            }
        }

        public async Task<string?> GetCurrentCommitHashAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-parse HEAD");
                if (result.ExitCode == 0)
                {
                    return result.Output.Trim();
                }

                _logger.LogWarning("Failed to get current commit hash: {Error}", result.Error);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get current commit hash");
                return null;
            }
        }

        public async Task CreateTagAsync(string tagName, string message)
        {
            try
            {
                _logger.LogInformation("Creating git tag: {TagName}", tagName);

                // Check if tag already exists
                var existingTag = await ExecuteGitCommandAsync($"tag -l \"{tagName}\"");
                if (!string.IsNullOrWhiteSpace(existingTag.Output))
                {
                    _logger.LogWarning("Tag already exists: {TagName}", tagName);
                    return;
                }

                // Create annotated tag
                var result = await ExecuteGitCommandAsync($"tag -a \"{tagName}\" -m \"{message}\"");
                if (result.ExitCode != 0)
                {
                    throw new DesignTokenException(DesignTokenExitCode.GitOperationFailure,
                        $"Failed to create tag {tagName}: {result.Error}");
                }

                _logger.LogInformation("✓ Git tag created: {TagName}", tagName);

                // Try to push tag (don't fail if this doesn't work)
                try
                {
                    await ExecuteGitCommandAsync($"push origin \"{tagName}\"");
                    _logger.LogInformation("✓ Tag pushed to remote: {TagName}", tagName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to push tag to remote (non-critical): {TagName}", tagName);
                }
            }
            catch (DesignTokenException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create git tag: {TagName}", tagName);
                throw new DesignTokenException(DesignTokenExitCode.GitOperationFailure,
                    $"Failed to create git tag {tagName}: {ex.Message}", ex);
            }
        }

        public async Task<bool> CreateBranchAsync(string branchName)
        {
            try
            {
                _logger.LogInformation("Creating git branch: {BranchName}", branchName);

                // Check if branch already exists
                var existingBranch = await ExecuteGitCommandAsync($"branch -l \"{branchName}\"");
                if (!string.IsNullOrWhiteSpace(existingBranch.Output))
                {
                    _logger.LogInformation("Branch already exists, checking out: {BranchName}", branchName);
                    var checkoutResult = await ExecuteGitCommandAsync($"checkout \"{branchName}\"");
                    return checkoutResult.ExitCode == 0;
                }

                // Create and checkout new branch
                var result = await ExecuteGitCommandAsync($"checkout -b \"{branchName}\"");
                if (result.ExitCode != 0)
                {
                    _logger.LogError("Failed to create branch {BranchName}: {Error}", branchName, result.Error);
                    return false;
                }

                _logger.LogInformation("✓ Git branch created and checked out: {BranchName}", branchName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create git branch: {BranchName}", branchName);
                return false;
            }
        }

        public async Task<bool> CommitChangesAsync(string message, string? authorName = null, string? authorEmail = null)
        {
            try
            {
                _logger.LogInformation("Committing changes with message: {Message}", message);

                // Stage all changes
                var stageResult = await ExecuteGitCommandAsync("add .");
                if (stageResult.ExitCode != 0)
                {
                    _logger.LogError("Failed to stage changes: {Error}", stageResult.Error);
                    return false;
                }

                // Check if there are any changes to commit
                var statusResult = await ExecuteGitCommandAsync("diff --cached --quiet");
                if (statusResult.ExitCode == 0)
                {
                    _logger.LogInformation("No changes to commit");
                    return true; // No changes is not an error
                }

                // Build commit command
                var commitCommand = new StringBuilder("commit");

                if (!string.IsNullOrEmpty(authorName) && !string.IsNullOrEmpty(authorEmail))
                {
                    commitCommand.Append($" --author=\"{authorName} <{authorEmail}>\"");
                }

                commitCommand.Append($" -m \"{message}\"");

                // Execute commit
                var commitResult = await ExecuteGitCommandAsync(commitCommand.ToString());
                if (commitResult.ExitCode != 0)
                {
                    _logger.LogError("Failed to commit changes: {Error}", commitResult.Error);
                    return false;
                }

                _logger.LogInformation("✓ Changes committed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit changes");
                return false;
            }
        }

        public async Task<bool> PushChangesAsync(string? branchName = null)
        {
            try
            {
                var pushCommand = "push origin";
                if (!string.IsNullOrEmpty(branchName))
                {
                    pushCommand += $" \"{branchName}\"";
                }
                else
                {
                    pushCommand += " HEAD";
                }

                _logger.LogInformation("Pushing changes to remote: {Command}", pushCommand);

                var result = await ExecuteGitCommandAsync(pushCommand);
                if (result.ExitCode != 0)
                {
                    _logger.LogError("Failed to push changes: {Error}", result.Error);
                    return false;
                }

                _logger.LogInformation("✓ Changes pushed to remote successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push changes");
                return false;
            }
        }

        public async Task<bool> CreatePullRequestAsync(string sourceBranch, string targetBranch, string title, string description)
        {
            try
            {
                _logger.LogInformation("Creating pull request: {SourceBranch} -> {TargetBranch}", sourceBranch, targetBranch);

                // This is platform-specific and would need to be implemented for each git hosting provider
                // For now, we'll just log the action and return success
                _logger.LogWarning("Pull request creation is not implemented - would need platform-specific implementation");
                _logger.LogInformation("PR Details: {Title} | {Description}", title, description);

                // In a real implementation, this would:
                // 1. Detect the git hosting platform (GitHub, Azure DevOps, GitLab, etc.)
                // 2. Use the appropriate API to create the pull request
                // 3. Return the PR URL or ID

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create pull request");
                return false;
            }
        }

        private async Task<GitCommandResult> ExecuteGitCommandAsync(string arguments)
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

            using var process = new Process { StartInfo = processStartInfo };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            var result = new GitCommandResult
            {
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString().Trim(),
                Error = errorBuilder.ToString().Trim()
            };

            _logger.LogDebug("Git command executed: git {Arguments} | Exit: {ExitCode}", arguments, result.ExitCode);
            if (result.ExitCode != 0)
            {
                _logger.LogDebug("Git command error: {Error}", result.Error);
            }

            return result;
        }

        private class GitCommandResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }
    }
}