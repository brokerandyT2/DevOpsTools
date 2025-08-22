using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements the logic for abstracting and providing access to the CI/CD pipeline's
    /// runtime environment variables. It checks a prioritized list of known variables for
    /// common CI/CD platforms and provides sensible defaults for local execution.
    /// </summary>
    public class EnvironmentService : IEnvironmentService
    {
        private readonly ILogger<EnvironmentService> _logger;

        // Cache the calculated Git range to avoid re-running the git process.
        private string? _cachedGitRange;

        private static readonly string[] WorkspacePathVars = { "BUILD_SOURCESDIRECTORY", "GITHUB_WORKSPACE", "CI_PROJECT_DIR" };
        private static readonly string[] ReleaseVersionVars = { "BUILD_BUILDNUMBER", "CI_COMMIT_TAG", "GITHUB_REF_NAME" };
        private static readonly string[] PipelineRunIdVars = { "BUILD_BUILDID", "GITHUB_RUN_ID", "CI_PIPELINE_ID" };
        private static readonly string[] GitHeadVars = { "BUILD_SOURCEVERSION", "GITHUB_SHA", "CI_COMMIT_SHA" };

        public EnvironmentService(ILogger<EnvironmentService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string GetWorkspacePath()
        {
            return GetFirstAvailableVariable(WorkspacePathVars, Directory.GetCurrentDirectory());
        }

        /// <inheritdoc />
        public string GetReleaseVersion()
        {
            return GetFirstAvailableVariable(ReleaseVersionVars, "0.0.0-local");
        }

        /// <inheritdoc />
        public string GetPipelineRunId()
        {
            return GetFirstAvailableVariable(PipelineRunIdVars, $"local-run-{DateTime.UtcNow.Ticks}");
        }

        /// <inheritdoc />
        public string GetGitRange()
        {
            // Return the cached value if already calculated.
            if (!string.IsNullOrEmpty(_cachedGitRange))
            {
                return _cachedGitRange;
            }

            var headCommit = GetFirstAvailableVariable(GitHeadVars, "HEAD");
            var workspacePath = GetWorkspacePath();
            var gitDirectory = Path.Combine(workspacePath, ".git");

            if (!Directory.Exists(gitDirectory))
            {
                _logger.LogWarning("Cannot determine Git range because '{Path}' is not a Git repository.", workspacePath);
                _cachedGitRange = headCommit; // Fallback to HEAD
                return _cachedGitRange;
            }

            try
            {
                // `git describe --tags --abbrev=0` gets the most recent tag reachable from the current commit.
                var latestTag = RunGitCommand(workspacePath, "describe --tags --abbrev=0");

                if (!string.IsNullOrEmpty(latestTag))
                {
                    _logger.LogInformation("Found previous Git tag: {Tag}. Creating commit range from tag to {Head}.", latestTag, headCommit);
                    _cachedGitRange = $"{latestTag}..{headCommit}";
                }
                else
                {
                    _logger.LogInformation("No previous Git tag found. Using single commit SHA as the Git range: {Head}", headCommit);
                    _cachedGitRange = headCommit;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to determine previous Git tag. Falling back to single commit SHA: {Head}", headCommit);
                _cachedGitRange = headCommit;
            }

            return _cachedGitRange;
        }

        private string GetFirstAvailableVariable(string[] variableNames, string defaultValue)
        {
            foreach (var name in variableNames)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _logger.LogDebug("Resolved environment value from variable '{VarName}': '{Value}'", name, value);
                    return value;
                }
            }
            _logger.LogDebug("No environment variable found in [{VarList}]. Falling back to default: '{DefaultValue}'", string.Join(", ", variableNames), defaultValue);
            return defaultValue;
        }

        private string RunGitCommand(string workspacePath, string arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workspacePath,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0 ? output.Trim() : string.Empty;
        }
    }
}