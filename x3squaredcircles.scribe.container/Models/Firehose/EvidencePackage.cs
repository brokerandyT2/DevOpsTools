using x3squaredcircles.scribe.container.Models.Forensic;

namespace x3squaredcircles.scribe.container.Models.Firehose
{
    /// <summary>
    /// Represents a single piece of evidence and its corresponding forensic data.
    /// This is a component of the main ReleaseArtifactData model for the JSON firehose.
    /// </summary>
    public class EvidencePackage
    {
        /// <summary>
        /// The human-readable name of the tool that produced the evidence.
        /// Example: "Risk Analysis"
        /// </summary>
        public string ToolName { get; }

        /// <summary>
        /// The standardized, final filename of the generated Markdown page for this evidence.
        /// Example: "2_-_Risk_Analysis.md"
        /// </summary>
        public string PageFileName { get; }

        /// <summary>
        /// The full, absolute path to the original, raw artifact file in the workspace.
        /// Example: "/src/workspace/risk-analysis.json"
        /// </summary>
        public string SourceFilePath { get; }

        /// <summary>
        /// The forensic log entry for the tool's execution. This can be null if no
        /// corresponding entry was found in pipeline-log.json.
        /// </summary>
        public LogEntry? ForensicLog { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EvidencePackage"/> class.
        /// </summary>
        /// <param name="toolName">The name of the tool.</param>
        /// <param name="pageFileName">The generated Markdown page filename.</param>
        /// <param name="sourceFilePath">The path to the raw evidence file.</param>
        /// <param name="forensicLog">The corresponding forensic log entry.</param>
        public EvidencePackage(string toolName, string pageFileName, string sourceFilePath, LogEntry? forensicLog)
        {
            ToolName = toolName;
            PageFileName = pageFileName;
            SourceFilePath = sourceFilePath;
            ForensicLog = forensicLog;
        }
    }
}