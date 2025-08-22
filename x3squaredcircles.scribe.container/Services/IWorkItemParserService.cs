using System.Collections.Generic;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for a service that parses work item IDs from Git commit messages.
    /// </summary>
    public interface IWorkItemParserService
    {
        /// <summary>
        /// Scans a raw string of Git log output and extracts all unique work item IDs
        /// based on a set of predefined regular expression patterns.
        /// </summary>
        /// <remarks>
        /// This method is designed to find common patterns such as:
        /// - JIRA-style: PROJ-123, TST-456
        /// - Azure DevOps-style: AB#12345, Bug #54321
        /// - GitHub-style: #123, gh-456
        /// The result is a collection of unique IDs to prevent duplicate processing.
        /// </remarks>
        /// <param name="rawGitLog">A string containing the multi-line output of a `git log` command.</param>
        /// <returns>An enumerable collection of unique work item IDs found in the log.</returns>
        IEnumerable<string> ParseWorkItemIdsFromLog(string rawGitLog);
    }
}