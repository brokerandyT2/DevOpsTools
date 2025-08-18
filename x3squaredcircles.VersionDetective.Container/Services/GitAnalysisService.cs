using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container.Services
{
    public interface IGitAnalysisService
    {
        Task<GitAnalysisResult> AnalyzeRepositoryAsync(VersionDetectiveConfiguration config);
        Task CreateTagAsync(string tagName, string message);
        Task<string?> GetLastSemanticVersionTagAsync();
        Task<bool> IsValidGitRepositoryAsync();
    }

    public class GitAnalysisService : IGitAnalysisService
    {
        private readonly ILogger<GitAnalysisService> _logger;
        private readonly string _workingDirectory;

        public GitAnalysisService(ILogger<GitAnalysisService> logger)
        {
            _logger = logger;
            _workingDirectory = "/src"; // Container mount point
        }

        public async Task<GitAnalysisResult> AnalyzeRepositoryAsync(VersionDetectiveConfiguration config)
        {
            try
            {
                _logger.LogInformation("Analyzing git repository in: {WorkingDirectory}", _workingDirectory);

                // Validate git repository
                if (!await IsValidGitRepositoryAsync())
                {
                    throw new VersionDetectiveException(VersionDetectiveExitCode.GitOperationFailure,
                        $"Not a valid git repository: {_workingDirectory}");
                }

                // Configure git authentication if needed
                await ConfigureGitAuthenticationAsync(config);

                // Get current commit
                var currentCommit = await GetCurrentCommitHashAsync();
                if (string.IsNullOrEmpty(currentCommit))
                {
                    throw new VersionDetectiveException(VersionDetectiveExitCode.GitOperationFailure,
                        "Unable to determine current commit hash");
                }

                // Determine baseline commit
                var baselineCommit = await DetermineBaselineCommitAsync(config);

                _logger.LogInformation("Current commit: {CurrentCommit}", currentCommit[..8]);
                _logger.LogInformation("Baseline commit: {BaselineCommit}", baselineCommit?[..8] ?? "none");

                // Get file changes since baseline
                var changes = await GetFileChangesSinceAsync(baselineCommit);
                _logger.LogInformation("Found {ChangeCount} changed files", changes.Count);

                // Get commit messages for analysis
                var commitMessages = await GetCommitMessagesSinceAsync(baselineCommit);
                _logger.LogInformation("Found {CommitCount} commits", commitMessages.Count);

                return new GitAnalysisResult
                {
                    CurrentCommit = currentCommit,
                    BaselineCommit = baselineCommit,
                    Changes = changes,
                    CommitMessages = commitMessages
                };
            }
            catch (VersionDetectiveException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Git analysis failed");
                throw new VersionDetectiveException(VersionDetectiveExitCode.GitOperationFailure,
                    $"Git analysis failed: {ex.Message}", ex);
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
                    throw new VersionDetectiveException(VersionDetectiveExitCode.GitOperationFailure,
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
            catch (VersionDetectiveException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create git tag: {TagName}", tagName);
                throw new VersionDetectiveException(VersionDetectiveExitCode.GitOperationFailure,
                    $"Failed to create git tag {tagName}: {ex.Message}", ex);
            }
        }

        public async Task<string?> GetLastSemanticVersionTagAsync()
        {
            try
            {
                // Look for semantic version tags in common patterns
                var patterns = new[] { "v*", "semver/*", "*.*.*" };

                foreach (var pattern in patterns)
                {
                    var result = await ExecuteGitCommandAsync($"tag -l \"{pattern}\" --sort=-version:refname");
                    if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
                    {
                        var tags = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        var semanticTag = tags.FirstOrDefault(IsSemanticVersionTag);
                        if (semanticTag != null)
                        {
                            _logger.LogDebug("Found semantic version tag: {Tag}", semanticTag);
                            return semanticTag;
                        }
                    }
                }

                _logger.LogDebug("No semantic version tags found");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get last semantic version tag");
                return null;
            }
        }

        public async Task<bool> IsValidGitRepositoryAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-parse --git-dir");
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task ConfigureGitAuthenticationAsync(VersionDetectiveConfiguration config)
        {
            try
            {
                // Set git user (required for some operations)
                await ExecuteGitCommandAsync("config user.name \"Version Detective\"");
                await ExecuteGitCommandAsync("config user.email \"version-detective@pipeline.local\"");

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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to configure git authentication (continuing anyway)");
            }
        }

        private async Task<string?> DetermineBaselineCommitAsync(VersionDetectiveConfiguration config)
        {
            // If FROM is specified, use that
            if (!string.IsNullOrEmpty(config.Analysis.FromCommit))
            {
                var isValidCommit = await IsValidCommitAsync(config.Analysis.FromCommit);
                if (isValidCommit)
                {
                    return config.Analysis.FromCommit;
                }
                else
                {
                    _logger.LogWarning("Invalid FROM commit specified: {FromCommit}", config.Analysis.FromCommit);
                }
            }

            // Try to find last semantic version tag
            var lastTag = await GetLastSemanticVersionTagAsync();
            if (lastTag != null)
            {
                var tagCommit = await GetTagCommitAsync(lastTag);
                if (tagCommit != null)
                {
                    return tagCommit;
                }
            }

            // Fallback: use first commit
            var firstCommit = await GetFirstCommitAsync();
            return firstCommit;
        }

        private async Task<List<GitFileChange>> GetFileChangesSinceAsync(string? baselineCommit)
        {
            var changes = new List<GitFileChange>();

            try
            {
                string gitCommand;
                if (baselineCommit != null)
                {
                    gitCommand = $"diff --name-status {baselineCommit}..HEAD";
                }
                else
                {
                    // If no baseline, get all files
                    gitCommand = "ls-files";
                }

                var result = await ExecuteGitCommandAsync(gitCommand);
                if (result.ExitCode != 0)
                {
                    _logger.LogWarning("Git diff failed: {Error}", result.Error);
                    return changes;
                }

                var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    try
                    {
                        GitFileChange change;

                        if (baselineCommit != null)
                        {
                            // Parse diff output: "M\tfile.cs" or "A\tfile.cs"
                            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                change = new GitFileChange
                                {
                                    FilePath = parts[1],
                                    ChangeType = MapGitChangeType(parts[0]),
                                    Content = await GetFileContentAsync(parts[1])
                                };
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // All files are considered "added" if no baseline
                            change = new GitFileChange
                            {
                                FilePath = line,
                                ChangeType = "Added",
                                Content = await GetFileContentAsync(line)
                            };
                        }

                        changes.Add(change);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to process git change line: {Line}", line);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get file changes since baseline");
            }

            return changes;
        }

        private async Task<List<string>> GetCommitMessagesSinceAsync(string? baselineCommit)
        {
            try
            {
                string gitCommand;
                if (baselineCommit != null)
                {
                    gitCommand = $"log --oneline --format=\"%s\" {baselineCommit}..HEAD";
                }
                else
                {
                    gitCommand = "log --oneline --format=\"%s\" -10"; // Last 10 commits
                }

                var result = await ExecuteGitCommandAsync(gitCommand);
                if (result.ExitCode != 0)
                {
                    return new List<string>();
                }

                return result.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get commit messages");
                return new List<string>();
            }
        }

        private async Task<string?> GetCurrentCommitHashAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-parse HEAD");
                return result.ExitCode == 0 ? result.Output.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> IsValidCommitAsync(string commit)
        {
            try
            {
                var result = await ExecuteGitCommandAsync($"rev-parse --verify {commit}");
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string?> GetTagCommitAsync(string tag)
        {
            try
            {
                var result = await ExecuteGitCommandAsync($"rev-list -n 1 {tag}");
                return result.ExitCode == 0 ? result.Output.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> GetFirstCommitAsync()
        {
            try
            {
                var result = await ExecuteGitCommandAsync("rev-list --max-parents=0 HEAD");
                return result.ExitCode == 0 ? result.Output.Split('\n').FirstOrDefault()?.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> GetFileContentAsync(string filePath)
        {
            try
            {
                var fullPath = Path.Combine(_workingDirectory, filePath);
                if (File.Exists(fullPath))
                {
                    return await File.ReadAllTextAsync(fullPath);
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string MapGitChangeType(string gitStatus)
        {
            return gitStatus.ToUpperInvariant() switch
            {
                "A" => "Added",
                "M" => "Modified",
                "D" => "Deleted",
                "R" => "Renamed",
                "C" => "Copied",
                _ => "Modified"
            };
        }

        private bool IsSemanticVersionTag(string tag)
        {
            // Remove common prefixes
            var version = tag;
            if (version.StartsWith("v")) version = version[1..];
            if (version.StartsWith("semver/")) version = version[7..];

            // Check if it matches semantic versioning pattern
            var parts = version.Split('.');
            return parts.Length >= 3 &&
                   parts.Take(3).All(part => int.TryParse(part, out _));
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

            return new GitCommandResult
            {
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString().Trim(),
                Error = errorBuilder.ToString().Trim()
            };
        }

        private class GitCommandResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }
    }
}