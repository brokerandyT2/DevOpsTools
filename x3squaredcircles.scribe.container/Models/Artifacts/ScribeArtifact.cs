namespace x3squaredcircles.scribe.container.Models.Artifacts
{
    /// <summary>
    /// Represents a single, discovered 3SC tool artifact in the workspace.
    /// This model serves as the bridge between a raw evidence file and the
    /// final, generated Markdown page in the release artifact.
    /// </summary>
    public class ScribeArtifact
    {
        /// <summary>
        /// The human-readable name of the tool, derived from the artifact's filename
        /// or other conventions. This is used for page titles and index entries.
        /// Example: "Risk Analysis"
        /// </summary>
        public string ToolName { get; }

        /// <summary>
        /// The full, absolute path to the original, raw artifact file in the workspace.
        /// Example: "/src/workspace/risk-analysis.json"
        /// </summary>
        public string SourceFilePath { get; }

        /// <summary>
        /// The standardized, final filename for the Markdown page that will be generated for this artifact.
        /// The page index is determined during the discovery process.
        /// Example: "2_-_Risk_Analysis.md"
        /// </summary>
        public string PageFileName { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScribeArtifact"/> class.
        /// </summary>
        /// <param name="toolName">The human-readable name of the tool.</param>
        /// <param name="sourceFilePath">The full path to the raw evidence file.</param>
        /// <param name="pageIndex">The numerical index for ordering the final pages.</param>
        public ScribeArtifact(string toolName, string sourceFilePath, int pageIndex)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new System.ArgumentException("Tool name cannot be null or whitespace.", nameof(toolName));

            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new System.ArgumentException("Source file path cannot be null or whitespace.", nameof(sourceFilePath));

            if (pageIndex < 2) // Index starts at 2, as 1 is reserved for Work Items.
                throw new System.ArgumentOutOfRangeException(nameof(pageIndex), "Page index must be 2 or greater.");

            ToolName = toolName;
            SourceFilePath = sourceFilePath;

            // Generate the standardized page name based on the specification.
            // e.g., (Risk Analysis) -> "2_-_Risk_Analysis.md"
            var sanitizedTitle = toolName.Replace(" ", "_");
            PageFileName = $"{pageIndex}_-_{sanitizedTitle}.md";
        }
    }
}