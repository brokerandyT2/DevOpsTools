using System;

namespace x3squaredcircles.scribe.container.Models.WorkItems
{
    /// <summary>
    /// Represents a single work item (e.g., a Jira issue, Azure DevOps work item, or GitHub issue).
    /// This model is designed to support both a "raw" state (with only an ID) and an "enriched"
    /// state (with full details fetched from a provider).
    /// </summary>
    public class WorkItem
    {
        /// <summary>
        /// The unique identifier of the work item.
        /// Example: "PROJ-1234", "AB#5678", "#90"
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The title or summary of the work item.
        /// This will be null or empty if the work item is not enriched.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// A direct, clickable URL to the work item in its provider system.
        /// This will be null if the work item is not enriched.
        /// </summary>
        public Uri? Url { get; set; }

        /// <summary>
        /// The type of the work item.
        /// Example: "Bug", "User Story", "Task"
        /// </summary>
        public string Type { get; set; } = "Unknown";

        /// <summary>
        /// A flag indicating whether the work item's details (Title, Url, etc.) have been
        /// successfully fetched from a provider.
        /// `false` indicates that this object only contains the raw ID parsed from a commit message.
        /// </summary>
        public bool IsEnriched { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkItem"/> class in its raw, unenriched state.
        /// </summary>
        /// <param name="id">The work item ID parsed from a source like a Git commit message.</param>
        public WorkItem(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Work item ID cannot be null or whitespace.", nameof(id));
            }

            Id = id;
            IsEnriched = false;
        }
    }
}