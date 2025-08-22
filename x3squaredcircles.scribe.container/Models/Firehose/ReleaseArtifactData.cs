using System;
using System.Collections.Generic;
using x3squaredcircles.scribe.container.Models.WorkItems;

namespace x3squaredcircles.scribe.container.Models.Firehose
{
    /// <summary>
    /// Represents the complete, structured data of a single release artifact.
    /// This is the root model that is serialized to JSON for the "firehose" log output,
    /// intended for machine consumption and automated processing.
    /// </summary>
    public class ReleaseArtifactData
    {
        /// <summary>
        /// The name of the application for this release.
        /// </summary>
        public string AppName { get; }

        /// <summary>
        /// The version of the release.
        /// </summary>
        public string ReleaseVersion { get; }

        /// <summary>
        /// The UTC timestamp of when the Scribe artifact was generated.
        /// </summary>
        public DateTime GenerationTimestampUtc { get; }

        /// <summary>
        /// The unique identifier for the pipeline run that generated this artifact.
        /// </summary>
        public string PipelineRunId { get; }

        /// <summary>
        /// The source Git commit or commit range for the release.
        /// </summary>
        public string GitRange { get; }

        /// <summary>
        /// The collection of discovered and potentially enriched work items (the "WHY").
        /// </summary>
        public IReadOnlyCollection<WorkItem> WorkItems { get; }

        /// <summary>
        /// The collection of all discovered evidence and its associated forensic data (the "WHAT" and "HOW").
        /// </summary>
        public IReadOnlyCollection<EvidencePackage> Evidence { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReleaseArtifactData"/> class.
        /// </summary>
        public ReleaseArtifactData(
            string appName,
            string releaseVersion,
            string pipelineRunId,
            string gitRange,
            IEnumerable<WorkItem> workItems,
            IEnumerable<EvidencePackage> evidence)
        {
            AppName = appName;
            ReleaseVersion = releaseVersion;
            PipelineRunId = pipelineRunId;
            GitRange = gitRange;
            GenerationTimestampUtc = DateTime.UtcNow;
            WorkItems = new List<WorkItem>(workItems);
            Evidence = new List<EvidencePackage>(evidence);
        }
    }
}