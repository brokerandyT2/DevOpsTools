using System.Collections.Generic;

namespace x3squaredcircles.scribe.container.Models.Artifacts
{
    /// <summary>
    /// Represents the complete result of the artifact discovery process.
    /// This model separates the identified pipeline definition file from the collection
    /// of generic 3SC tool artifacts.
    /// </summary>
    public class DiscoveryResult
    {
        /// <summary>
        /// The collection of generic 3SC tool artifacts found in the workspace.
        /// </summary>
        public IEnumerable<ScribeArtifact> Artifacts { get; }

        /// <summary>
        /// The full path to the native pipeline definition file (e.g., azure-pipelines.yml).
        /// This will be null if no pipeline file could be identified.
        /// </summary>
        public string? PipelineFilePath { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscoveryResult"/> class.
        /// </summary>
        /// <param name="artifacts">The collection of generic tool artifacts.</param>
        /// <param name="pipelineFilePath">The path to the identified pipeline file.</param>
        public DiscoveryResult(IEnumerable<ScribeArtifact> artifacts, string? pipelineFilePath)
        {
            Artifacts = artifacts;
            PipelineFilePath = pipelineFilePath;
        }
    }
}