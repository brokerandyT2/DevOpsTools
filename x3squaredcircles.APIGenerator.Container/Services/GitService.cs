using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Defines the contract for a service that interacts with Git repositories.
    /// </summary>
    public interface IGitService
    {
        /// <summary>
        /// Finds the most recent Git tag in a remote repository that matches a given pattern.
        /// </summary>
        Task<string> GetLatestTagAsync(string repoUrl, string pat, string pattern);

        /// <summary>
        /// Clones a specific tag of a Git repository to a temporary local path.
        /// </summary>
        Task<string> CloneRepoAsync(string repoUrl, string pat, string tagOrBranch);

        /// <summary>
        /// Commits all changes in a local directory to a remote repository and applies a tag.
        /// </summary>
        Task CommitAndPushAsync(string localPath, string repoUrl, string pat, string tag, string commitMessage);
    }

    /// <summary>
    /// Implements Git operations by shelling out to the git command-line tool.
    /// Assumes 'git' is installed in the container environment.
    /// </summary>
    public class GitService : IGitService
    {
        private readonly IAppLogger _logger;
        private readonly string _tempDirectory;

        public GitService(IAppLogger logger)
        {
            _logger = logger;
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"datalink-git-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectory);
        }

        public async Task<string> GetLatestTagAsync(string repoUrl, string pat, string pattern)
        {
            _logger.LogDebug($"Fetching latest tag from {repoUrl} matching pattern '{pattern}'");

            var authenticatedUrl = GetAuthenticatedUrl(repoUrl, pat);
            var arguments = $"ls-remote --tags --sort=-v:refname \"{authenticatedUrl}\" \"{pattern}\"";

            var result = await ExecuteGitCommandAsync(arguments, _tempDirectory, pat);

            if (!result.Success)
            {
                throw new DataLinkException(ExitCode.GitOperationFailed, "GIT_TAG_FETCH_FAILED", $"Failed to fetch tags from {repoUrl}. Error: {result.Error}");
            }

            var latestTagLine = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(latestTagLine))
            {
                throw new DataLinkException(ExitCode.GitOperationFailed, "GIT_NO_TAGS_FOUND", $"No tags matching pattern '{pattern}' found in repository {repoUrl}.");
            }

            var tagName = latestTagLine.Split('/').LastOrDefault();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                throw new DataLinkException(ExitCode.GitOperationFailed, "GIT_TAG_PARSE_FAILED", $"Could not parse tag from ls-remote output: {latestTagLine}");
            }

            _logger.LogInfo($"Discovered latest version tag: {tagName}");
            return tagName;
        }

        public async Task<string> CloneRepoAsync(string repoUrl, string pat, string tagOrBranch)
        {
            var localPath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(localPath);
            _logger.LogDebug($"Cloning {repoUrl} at ref '{tagOrBranch}' to {localPath}");

            var authenticatedUrl = GetAuthenticatedUrl(repoUrl, pat);
            var arguments = $"clone --branch {tagOrBranch} --depth 1 \"{authenticatedUrl}\" \"{localPath}\"";

            var result = await ExecuteGitCommandAsync(arguments, _tempDirectory, pat);
            if (!result.Success)
            {
                throw new DataLinkException(ExitCode.GitOperationFailed, "GIT_CLONE_FAILED", $"Failed to clone repository {repoUrl} at ref '{tagOrBranch}'. Error: {result.Error}");
            }

            return localPath;
        }

        public async Task CommitAndPushAsync(string localPath, string repoUrl, string pat, string tag, string commitMessage)
        {
            _logger.LogInfo($"Committing changes to destination repository and tagging as {tag}");

            await ExecuteGitCommandAsync("config --global user.email \"datalink@3sc.com\"", localPath, pat);
            await ExecuteGitCommandAsync("config --global user.name \"3SC DataLink Generator\"", localPath, pat);

            var authenticatedUrl = GetAuthenticatedUrl(repoUrl, pat);
            await ExecuteGitCommandAsync($"remote set-url origin \"{authenticatedUrl}\"", localPath, pat);

            var addResult = await ExecuteGitCommandAsync("add -A", localPath, pat);
            if (!addResult.Success) throw new DataLinkException(ExitCode.GitOperationFailed, "GIT_ADD_FAILED", $"git add failed: {addResult.Error}");

            var commitResult = await ExecuteGitCommandAsync($"commit -m \"{commitMessage}\"", localPath, pat);
            if (!commitResult.Success && !commitResult.Output.Contains("nothing to commit") && !commitResult.Error.Contains("nothing to commit"))
            {
                throw new DataLinkException(ExitCode.GitOperationFailed, "GIT_COMMIT_FAILED", $"git commit failed: {commitResult.Error}");
            }

            var pushResult = await ExecuteGitCommandAsync("push origin HEAD:main", localPath, pat);
            if (!pushResult.Success) throw new DataLinkException(ExitCode.GitOperationFailed, "GIT_PUSH_FAILED", $"git push failed: {pushResult.Error}");

            var tagResult = await ExecuteGitCommandAsync($"tag -f -a {tag} -m \"Generated from source tag {tag}\"", localPath, pat);
            if (!tagResult.Success) throw new DataLinkException(ExitCode.GitOperationFailed, "GIT_TAG_FAILED", $"git tag failed: {tagResult.Error}");

            var pushTagResult = await ExecuteGitCommandAsync($"push origin {tag} --force", localPath, pat);
            if (!pushTagResult.Success) throw new DataLinkException(ExitCode.GitOperationFailed, "GIT_PUSH_TAG_FAILED", $"git push tag failed: {pushTagResult.Error}");

            _logger.LogInfo($"✓ Successfully committed and tagged changes to {repoUrl}");
        }

        private string GetAuthenticatedUrl(string repoUrl, string pat)
        {
            var uri = new Uri(repoUrl);
            return $"{uri.Scheme}://x-access-token:{pat}@{uri.Host}{uri.AbsolutePath}";
        }

        private async Task<(bool Success, string Output, string Error)> ExecuteGitCommandAsync(string arguments, string workingDirectory, string patToMask)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using var outputWaitHandle = new AutoResetEvent(false);
            using var errorWaitHandle = new AutoResetEvent(false);

            process.OutputDataReceived += (_, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); else outputWaitHandle.Set(); };
            process.ErrorDataReceived += (_, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); else errorWaitHandle.Set(); };

            var safeArguments = arguments.Replace(patToMask, "[REDACTED_PAT]");
            _logger.LogDebug($"Executing git command: git {safeArguments}");

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            outputWaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            errorWaitHandle.WaitOne(TimeSpan.FromSeconds(10));

            var output = outputBuilder.ToString().Trim();
            var error = errorBuilder.ToString().Trim();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning($"Git command failed. Exit Code: {process.ExitCode}, Stderr: {error}");
                return (false, output, error);
            }

            return (true, output, error);
        }
    }
}