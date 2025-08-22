using System.Collections.Generic;
using x3squaredcircles.scribe.container.Models.Artifacts;
using x3squaredcircles.scribe.container.Models.Forensic;
using x3squaredcircles.scribe.container.Models.WorkItems;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for a service that generates Markdown content for the release artifact.
    /// </summary>
    public interface IMarkdownGenerationService
    {
        /// <summary>
        /// Generates the Markdown content for the main "Front Page" (1_-_Work_Items.md).
        /// </summary>
        /// <param name="releaseVersion">The version of the release.</param>
        /// <param name="pipelineRunId">The unique identifier for the pipeline run.</param>
        /// <param name="gitRange">The source Git commit range for the release.</param>
        /// <param name="workItems">The collection of discovered and potentially enriched work items.</param>
        /// <param name="discoveredArtifacts">The collection of all other discovered artifacts to be indexed.</param>
        /// <param name="pipelinePageName">The filename of the generated pipeline page, if one exists.</param>
        /// <returns>A string containing the complete Markdown content for the work items page.</returns>
        string GenerateWorkItemsPage(
            string releaseVersion,
            string pipelineRunId,
            string gitRange,
            IEnumerable<WorkItem> workItems,
            IEnumerable<ScribeArtifact> discoveredArtifacts,
            string? pipelinePageName);

        /// <summary>
        /// Generates the Markdown content for a standard tool evidence sub-page.
        /// </summary>
        /// <param name="artifact">The artifact for which to generate the page.</param>
        /// <param name="logEntry">The corresponding forensic log entry for the tool's execution (can be null).</param>
        /// <returns>A string containing the complete Markdown content for the sub-page.</returns>
        string GenerateToolSubPage(ScribeArtifact artifact, LogEntry? logEntry);

        /// <summary>
        /// Generates the Markdown content for the dedicated Pipeline Definition page.
        /// </summary>
        /// <param name="pipelineFilePath">The full path to the raw pipeline definition file.</param>
        /// <returns>A string containing the complete Markdown content for the pipeline page.</returns>
        string GeneratePipelinePage(string pipelineFilePath);
    }
}