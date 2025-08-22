using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements the logic for parsing work item IDs from Git commit messages.
    /// </summary>
    public class WorkItemParserService : IWorkItemParserService
    {
        private readonly ILogger<WorkItemParserService> _logger;

        // A pre-compiled list of regular expressions to find common work item ID formats.
        // Compiling them improves performance for repeated use.
        private static readonly IReadOnlyList<Regex> WorkItemPatterns = new List<Regex>
        {
            // JIRA-style (e.g., PROJ-1234, ABC-123)
            new Regex(@"\b([A-Z][A-Z0-9]+-\d+)\b", RegexOptions.Compiled),

            // Azure DevOps-style (e.g., AB#12345, Bug #54321, Task #987)
            new Regex(@"\b(?:AB|Bug|Task|Epic|Feature)\s*#\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // GitHub-style (e.g., #123, GH-456)
            new Regex(@"\b(?:GH-?|#)(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkItemParserService"/> class.
        /// </summary>
        /// <param name="logger">The logger for forensic and operational messages.</param>
        public WorkItemParserService(ILogger<WorkItemParserService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public IEnumerable<string> ParseWorkItemIdsFromLog(string rawGitLog)
        {
            if (string.IsNullOrEmpty(rawGitLog))
            {
                _logger.LogInformation("Raw Git log is empty. No work items to parse.");
                return Enumerable.Empty<string>();
            }

            // Using a HashSet ensures that we only store and return unique work item IDs.
            var foundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("Parsing Git log for work item IDs using {Count} patterns.", WorkItemPatterns.Count);

            foreach (var pattern in WorkItemPatterns)
            {
                var matches = pattern.Matches(rawGitLog);
                foreach (Match match in matches)
                {
                    if (match.Success)
                    {
                        // Some patterns might have multiple capture groups. We prioritize the last
                        // non-empty group, but fall back to the whole match value.
                        // e.g., for `(AB#)(\d+)`, Group[2] would be `\d+`, but for `(PROJ-123)`, Group[1] is the whole thing.
                        // Taking the full match value is the safest and most consistent approach.
                        var id = match.Value;
                        if (foundIds.Add(id))
                        {
                            _logger.LogDebug("Discovered work item ID: {WorkItemId}", id);
                        }
                    }
                }
            }

            _logger.LogInformation("Discovered {Count} unique work item IDs.", foundIds.Count);

            return foundIds;
        }
    }
}