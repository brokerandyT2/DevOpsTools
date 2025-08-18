using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IGitOperationsService
    {
        Task<bool> IsValidGitRepositoryAsync();
        Task ConfigureGitAuthenticationAsync(SqlSchemaConfiguration config);
        Task<string> GetCurrentBranchAsync();
        Task<string> GetCurrentCommitHashAsync();
        Task<string> GetCurrentCommitHashFullAsync();
        Task<string> GetRepositoryNameAsync();
        Task<bool> IsWorkingDirectoryCleanAsync();
        Task CreateTagAsync(string tagName, string message);
        Task<bool> TagExistsAsync(string tagName);
        Task DeleteTagAsync(string tagName);
        Task PushTagAsync(string tagName);
        Task<string> GetLastTagAsync();
        Task<string> GetRemoteUrlAsync();
        Task<bool> IsBranchUpToDateAsync();
        Task<DateTime> GetLastCommitDateAsync();
        Task<string> GetLastCommitAuthorAsync();
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
                return result.Success && !string.IsNullOrEmpty(result.Output);
            }
            catch
            {
                return false;
            }
        }

        public async Task ConfigureGitAuthenticationAsync(SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogDebug("Configuring git authentication");

                // Configure git user if not already set (required for tagging)
                await ConfigureGitUserAsync();

                // Configure authentication based on available tokens
                if (!string.IsNullOrEmpty(config.Authentication.PatToken))
                {
                    await ConfigurePatAuthenticationAsync(config.Authentication.PatToken, config.RepoUrl);
                    _logger.LogDebug("Configured PAT authentication for git operations");
                }
                else if (!string.IsNullOrEmpty(config.Authentication.PipelineToken))
                {
                    await ConfigurePipelineAuthenticationAsync(config.Authentication.PipelineToken, config.RepoUrl);
                    _logger.LogDebug("Configured pipeline token authentication for git operations");
                }
                else
                {
                    _logger.LogWarning("No authentication token available - git operations may fail for private repositories");
                }

                // Verify authentication works
                await VerifyGitAuthenticationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure git authentication");
                throw new SqlSchemaException(SqlSchemaExitCode.AuthenticationFailure,
                    $"Failed to configure git authentication: {ex.Message}", ex);
            }
        }

        public async Task<string> GetCurrentBranchAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-parse --abbrev-ref HEAD");
                if (result.Success)
                {
                    return result.Output.Trim();
                }

                // Fallback for detached HEAD (CI/CD scenarios)
                var branchResult = await ExecuteGitCommandAsync("branch --show-current");
                if (branchResult.Success && !string.IsNullOrEmpty(branchResult.Output))
                {
                    return branchResult.Output.Trim();
                }

                // Try to get branch from environment variables (CI/CD)
                var ciBranch = GetBranchFromEnvironment();
                if (!string.IsNullOrEmpty(ciBranch))
                {
                    return ciBranch;
                }

                return "HEAD";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get current branch, defaulting to 'HEAD'");
                return "HEAD";
            }
        }

        public async Task<string> GetCurrentCommitHashAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-parse --short HEAD");
                return result.Success ? result.Output.Trim() : "unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get current commit hash");
                return "unknown";
            }
        }

        public async Task<string> GetCurrentCommitHashFullAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-parse HEAD");
                return result.Success ? result.Output.Trim() : "unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get current full commit hash");
                return "unknown";
            }
        }

        public async Task<string> GetRepositoryNameAsync()
        {
            try
            {
                // Try to get from remote URL first
                var remoteUrl = await GetRemoteUrlAsync();
                if (!string.IsNullOrEmpty(remoteUrl))
                {
                    return ExtractRepoNameFromUrl(remoteUrl);
                }

                // Fallback to directory name
                var dirInfo = new DirectoryInfo(_workingDirectory);
                return dirInfo.Name;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get repository name");
                return "unknown-repo";
            }
        }

        public async Task<bool> IsWorkingDirectoryCleanAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("status --porcelain");
                return result.Success && string.IsNullOrWhiteSpace(result.Output);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check working directory status");
                return false;
            }
        }

        public async Task CreateTagAsync(string tagName, string message)
        {
            try
            {
                _logger.LogInformation("Creating git tag: {TagName}", tagName);

                // Check if tag already exists
                if (await TagExistsAsync(tagName))
                {
                    _logger.LogWarning("Tag {TagName} already exists, skipping creation", tagName);
                    return;
                }

                // Create annotated tag
                var result = await ExecuteGitCommandAsync($"tag -a \"{tagName}\" -m \"{message}\"");
                if (!result.Success)
                {
                    throw new InvalidOperationException($"Failed to create tag: {result.Error}");
                }

                _logger.LogInformation("✓ Git tag created: {TagName}", tagName);

                // Try to push tag (non-blocking)
                try
                {
                    await PushTagAsync(tagName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to push tag {TagName} - this may be expected in some environments", tagName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create git tag: {TagName}", tagName);
                throw new SqlSchemaException(SqlSchemaExitCode.GitOperationFailure,
                    $"Failed to create git tag '{tagName}': {ex.Message}", ex);
            }
        }

        public async Task<bool> TagExistsAsync(string tagName)
        {
            try
            {
                var result = await ExecuteGitCommandAsync($"tag -l \"{tagName}\"");
                return result.Success && !string.IsNullOrWhiteSpace(result.Output);
            }
            catch
            {
                return false;
            }
        }

        public async Task DeleteTagAsync(string tagName)
        {
            try
            {
                _logger.LogInformation("Deleting git tag: {TagName}", tagName);

                var result = await ExecuteGitCommandAsync($"tag -d \"{tagName}\"");
                if (!result.Success)
                {
                    throw new InvalidOperationException($"Failed to delete tag: {result.Error}");
                }

                _logger.LogInformation("✓ Git tag deleted: {TagName}", tagName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete git tag: {TagName}", tagName);
                throw;
            }
        }

        public async Task PushTagAsync(string tagName)
        {
            try
            {
                _logger.LogDebug("Pushing git tag: {TagName}", tagName);

                var result = await ExecuteGitCommandAsync($"push origin \"{tagName}\"");
                if (!result.Success)
                {
                    throw new InvalidOperationException($"Failed to push tag: {result.Error}");
                }

                _logger.LogDebug("✓ Git tag pushed: {TagName}", tagName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push git tag: {TagName}", tagName);
                throw;
            }
        }

        public async Task<string> GetLastTagAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("describe --tags --abbrev=0");
                return result.Success ? result.Output.Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public async Task<string> GetRemoteUrlAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("config --get remote.origin.url");
                return result.Success ? result.Output.Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public async Task<bool> IsBranchUpToDateAsync()
        {
            try
            {
                // Fetch latest changes
                await ExecuteGitCommandAsync("fetch origin");

                // Check if local is behind remote
                var result = await ExecuteGitCommandAsync("rev-list HEAD..@{u} --count");
                if (result.Success && int.TryParse(result.Output.Trim(), out var behindCount))
                {
                    return behindCount == 0;
                }

                return true; // Assume up to date if we can't determine
            }
            catch
            {
                return true; // Assume up to date if we can't check
            }
        }

        public async Task<DateTime> GetLastCommitDateAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("log -1 --format=%ci");
                if (result.Success && DateTime.TryParse(result.Output.Trim(), out var commitDate))
                {
                    return commitDate.ToUniversalTime();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get last commit date");
            }

            return DateTime.UtcNow;
        }

        public async Task<string> GetLastCommitAuthorAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("log -1 --format=%an");
                return result.Success ? result.Output.Trim() : "unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get last commit author");
                return "unknown";
            }
        }

        private async Task ConfigureGitUserAsync()
        {
            try
            {
                // Check if user is already configured
                var nameResult = await ExecuteGitCommandAsync("config user.name");
                var emailResult = await ExecuteGitCommandAsync("config user.email");

                if (!nameResult.Success || string.IsNullOrWhiteSpace(nameResult.Output))
                {
                    await ExecuteGitCommandAsync("config user.name \"SQL Schema Generator\"");
                    _logger.LogDebug("Set git user.name to default value");
                }

                if (!emailResult.Success || string.IsNullOrWhiteSpace(emailResult.Output))
                {
                    await ExecuteGitCommandAsync("config user.email \"sql-schema-generator@pipeline.local\"");
                    _logger.LogDebug("Set git user.email to default value");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to configure git user, operations may fail");
            }
        }

        private async Task ConfigurePatAuthenticationAsync(string patToken, string repoUrl)
        {
            try
            {
                var uri = new Uri(repoUrl);
                var host = uri.Host;

                // Configure credential helper for the specific host
                if (host.Contains("github.com"))
                {
                    await ExecuteGitCommandAsync($"config credential.https://{host}.username {patToken}");
                    await ExecuteGitCommandAsync($"config credential.https://{host}.password \"\"");
                }
                else if (host.Contains("dev.azure.com") || host.Contains("visualstudio.com"))
                {
                    await ExecuteGitCommandAsync($"config credential.https://{host}.username \"PAT\"");
                    await ExecuteGitCommandAsync($"config credential.https://{host}.password {patToken}");
                }
                else
                {
                    // Generic git hosting
                    await ExecuteGitCommandAsync($"config credential.https://{host}.username {patToken}");
                    await ExecuteGitCommandAsync($"config credential.https://{host}.password \"\"");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to configure PAT authentication");
                throw;
            }
        }

        private async Task ConfigurePipelineAuthenticationAsync(string pipelineToken, string repoUrl)
        {
            try
            {
                var uri = new Uri(repoUrl);
                var host = uri.Host;

                if (host.Contains("dev.azure.com") || host.Contains("visualstudio.com"))
                {
                    // Azure DevOps - use System.AccessToken
                    await ExecuteGitCommandAsync($"config credential.https://{host}.username \"System\"");
                    await ExecuteGitCommandAsync($"config credential.https://{host}.password {pipelineToken}");
                }
                else if (host.Contains("github.com"))
                {
                    // GitHub Actions - use GITHUB_TOKEN
                    await ExecuteGitCommandAsync($"config credential.https://{host}.username \"x-access-token\"");
                    await ExecuteGitCommandAsync($"config credential.https://{host}.password {pipelineToken}");
                }
                else
                {
                    // Generic - treat as bearer token
                    await ExecuteGitCommandAsync($"config credential.https://{host}.username \"token\"");
                    await ExecuteGitCommandAsync($"config credential.https://{host}.password {pipelineToken}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to configure pipeline authentication");
                throw;
            }
        }

        private async Task VerifyGitAuthenticationAsync()
        {
            try
            {
                // Try a simple fetch to verify authentication works
                var result = await ExecuteGitCommandAsync("fetch --dry-run");
                if (!result.Success && result.Error.Contains("Authentication failed"))
                {
                    throw new InvalidOperationException("Git authentication verification failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Git authentication verification failed - operations may still work");
            }
        }

        private string GetBranchFromEnvironment()
        {
            // Azure DevOps
            var azureBranch = Environment.GetEnvironmentVariable("BUILD_SOURCEBRANCHNAME") ??
                             Environment.GetEnvironmentVariable("BUILD_SOURCEBRANCH");
            if (!string.IsNullOrEmpty(azureBranch))
            {
                return azureBranch.Replace("refs/heads/", "");
            }

            // GitHub Actions
            var githubBranch = Environment.GetEnvironmentVariable("GITHUB_REF_NAME") ??
                              Environment.GetEnvironmentVariable("GITHUB_HEAD_REF");
            if (!string.IsNullOrEmpty(githubBranch))
            {
                return githubBranch;
            }

            // Jenkins
            var jenkinsBranch = Environment.GetEnvironmentVariable("BRANCH_NAME") ??
                               Environment.GetEnvironmentVariable("GIT_BRANCH");
            if (!string.IsNullOrEmpty(jenkinsBranch))
            {
                return jenkinsBranch.Replace("origin/", "");
            }

            // GitLab CI
            var gitlabBranch = Environment.GetEnvironmentVariable("CI_COMMIT_REF_NAME");
            if (!string.IsNullOrEmpty(gitlabBranch))
            {
                return gitlabBranch;
            }

            return string.Empty;
        }

        private string ExtractRepoNameFromUrl(string repoUrl)
        {
            try
            {
                var uri = new Uri(repoUrl);
                var path = uri.AbsolutePath.Trim('/');

                // Remove .git suffix if present
                if (path.EndsWith(".git"))
                {
                    path = path.Substring(0, path.Length - 4);
                }

                // Get the last part of the path (repository name)
                var parts = path.Split('/');
                return parts.Length > 0 ? parts[parts.Length - 1] : "unknown-repo";
            }
            catch
            {
                return "unknown-repo";
            }
        }

        private async Task<GitCommandResult> ExecuteGitCommandAsync(string arguments)
        {
            try
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

                _logger.LogDebug("Executing git command: git {Arguments}", arguments);

                using var process = new Process { StartInfo = processStartInfo };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        outputBuilder.AppendLine(args.Data);
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        errorBuilder.AppendLine(args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                var output = outputBuilder.ToString().Trim();
                var error = errorBuilder.ToString().Trim();
                var success = process.ExitCode == 0;

                if (!success)
                {
                    _logger.LogDebug("Git command failed: {Arguments}, Exit Code: {ExitCode}, Error: {Error}",
                        arguments, process.ExitCode, error);
                }

                return new GitCommandResult
                {
                    Success = success,
                    ExitCode = process.ExitCode,
                    Output = output,
                    Error = error
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute git command: {Arguments}", arguments);
                return new GitCommandResult
                {
                    Success = false,
                    ExitCode = -1,
                    Output = string.Empty,
                    Error = ex.Message
                };
            }
        }

        private class GitCommandResult
        {
            public bool Success { get; set; }
            public int ExitCode { get; set; }
            public string Output { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }
    }
}