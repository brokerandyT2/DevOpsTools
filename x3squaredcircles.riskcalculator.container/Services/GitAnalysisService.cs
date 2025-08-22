using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    /// <summary>
    /// Implements the logic for interacting with the Git repository using command-line operations.
    /// </summary>
    public class GitAnalysisService : IGitAnalysisService
    {
        private readonly ILogger<GitAnalysisService> _logger;
        private readonly string _workingDirectory;
        private const string LastRunTagName = "change-analysis-last-run";

        public GitAnalysisService(ILogger<GitAnalysisService> logger)
        {
            _logger = logger;
            _workingDirectory = "/src";
        }

        public async Task<GitDelta> GetDeltaSinceLastRunAsync()
        {
            var fromCommit = await GetLastRunCommitHashAsync();
            var toCommit = await GetCurrentCommitHashAsync("HEAD");

            if (string.IsNullOrWhiteSpace(toCommit))
            {
                throw new RiskCalculatorException(RiskCalculatorExitCode.GitOperationFailure, "Could not determine the current HEAD commit hash.");
            }

            var delta = new GitDelta
            {
                FromCommit = fromCommit,
                ToCommit = toCommit
            };

            var gitLogCommand = string.IsNullOrWhiteSpace(fromCommit)
                ? "log --name-status --pretty=format:\"commit:%H\"" // Initial run: get all history
                : $"log {fromCommit}..{toCommit} --name-status --pretty=format:\"commit:%H\"";

            _logger.LogInformation("Analyzing Git history from '{From}' to '{To}'...", fromCommit ?? "repository root", toCommit.Substring(0, 7));
            var (success, output, error) = await ExecuteGitCommandAsync(gitLogCommand);

            if (!success)
            {
                throw new RiskCalculatorException(RiskCalculatorExitCode.GitOperationFailure, $"Git log failed: {error}");
            }

            ParseGitLogOutput(output, delta);
            return delta;
        }

        public async Task CommitAnalysisStateAndMoveTagAsync(string analysisFilePath)
        {
            _logger.LogInformation("Committing analysis state file and updating tag...");

            var relativePath = Path.GetRelativePath(_workingDirectory, analysisFilePath);

            // Stage the analysis file
            var (addSuccess, _, addError) = await ExecuteGitCommandAsync($"add \"{relativePath}\"");
            if (!addSuccess)
            {
                throw new RiskCalculatorException(RiskCalculatorExitCode.GitOperationFailure, $"Failed to stage analysis file: {addError}");
            }

            // Commit the file
            var commitMessage = $"chore(risk-analysis): update change-analysis.json [skip ci]";
            var (commitSuccess, _, commitError) = await ExecuteGitCommandAsync($"commit -m \"{commitMessage}\"");
            if (!commitSuccess)
            {
                if (commitError.Contains("nothing to commit"))
                {
                    _logger.LogInformation("No changes to analysis file, commit skipped.");
                }
                else
                {
                    throw new RiskCalculatorException(RiskCalculatorExitCode.GitOperationFailure, $"Failed to commit analysis file: {commitError}");
                }
            }
            else
            {
                _logger.LogInformation("Committed updated analysis file.");
            }

            // Get the hash of the new commit
            var newCommitHash = await GetCurrentCommitHashAsync("HEAD");

            // Forcibly move the tag to the new commit
            var (tagSuccess, _, tagError) = await ExecuteGitCommandAsync($"tag -f {LastRunTagName} {newCommitHash}");
            if (!tagSuccess)
            {
                throw new RiskCalculatorException(RiskCalculatorExitCode.GitOperationFailure, $"Failed to move tag '{LastRunTagName}': {tagError}");
            }

            // Push the changes and the tag
            var (pushSuccess, _, pushError) = await ExecuteGitCommandAsync("push origin HEAD");
            if (!pushSuccess)
            {
                throw new RiskCalculatorException(RiskCalculatorExitCode.GitOperationFailure, $"Failed to push commit: {pushError}");
            }

            var (pushTagSuccess, _, pushTagError) = await ExecuteGitCommandAsync($"push origin {LastRunTagName} --force");
            if (!pushTagSuccess)
            {
                throw new RiskCalculatorException(RiskCalculatorExitCode.GitOperationFailure, $"Failed to push tag '{LastRunTagName}': {pushTagError}");
            }

            _logger.LogInformation("Successfully committed and pushed analysis state, tag moved to {NewCommitHash}.", newCommitHash.Substring(0, 7));
        }

        private void ParseGitLogOutput(string output, GitDelta delta)
        {
            var commits = output.Split("commit:", StringSplitOptions.RemoveEmptyEntries);
            var changesByPath = new Dictionary<string, GitFileChange>();

            foreach (var commitBlock in commits)
            {
                var lines = commitBlock.Trim().Split('\n');
                if (lines.Length < 2) continue; // Malformed block

                // Line 0 is the commit hash
                // Lines 1+ are the file statuses

                var filesInThisCommit = new List<string>();

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    var status = parts[0];
                    var path = parts.Length > 2 ? parts[2] : parts[1]; // Handle renames (R089    oldpath    newpath)

                    if (!changesByPath.TryGetValue(path, out var fileChange))
                    {
                        fileChange = new GitFileChange { Path = path, Status = status };
                        changesByPath[path] = fileChange;
                    }

                    filesInThisCommit.Add(path);
                }

                // For blast radius, add all files changed in this commit
                if (filesInThisCommit.Count > 1)
                {
                    delta.CochangedPaths.AddRange(filesInThisCommit);
                }
            }
            delta.Changes = changesByPath.Values.ToList();
        }

        private async Task<string> GetLastRunCommitHashAsync()
        {
            var (success, output, _) = await ExecuteGitCommandAsync($"rev-parse {LastRunTagName}");
            return success ? output : null;
        }

        private async Task<string> GetCurrentCommitHashAsync(string rev)
        {
            var (success, output, _) = await ExecuteGitCommandAsync($"rev-parse {rev}");
            return success ? output : null;
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
                    CreateNoWindow = true,
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
                _logger.LogDebug("Git command failed: git {Arguments}. Exit Code: {ExitCode}. Error: {Error}", arguments, process.ExitCode, error);
                return (false, output, error);
            }

            return (true, output, error);
        }
    }
}