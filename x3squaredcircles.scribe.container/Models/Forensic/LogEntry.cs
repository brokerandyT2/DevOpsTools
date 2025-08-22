using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace x3squaredcircles.scribe.container.Models.Forensic
{
    /// <summary>
    '// Represents a single entry in the pipeline-log.json artifact, as mandated
    /// by the 3SC Forensic Logging Protocol.
    /// </summary>
    public class LogEntry
    {
        /// <summary>
        /// The official name of the 3SC tool that generated this log entry.
        /// e.g., "3SC.Gate", "3SC.Forge"
        /// </summary>
        [JsonPropertyName("toolName")]
        public string ToolName { get; set; } = string.Empty;

        /// <summary>
        /// The specific version of the tool that was executed.
        /// e.g., "2.1.3"
        /// </summary>
        [JsonPropertyName("toolVersion")]
        public string ToolVersion { get; set; } = string.Empty;

        /// <summary>
        /// The UTC timestamp of when the tool's execution began.
        /// </summary>
        [JsonPropertyName("executionTimestamp")]
        public DateTime ExecutionTimestamp { get; set; }

        /// <summary>
        /// A snapshot of the tool's configuration at the time of execution.
        /// This is a collection of all consumed environment variables, limited to those with the
        /// universal 3SC_ prefix or the tool's specific TOOLNAME_ prefix.
        /// </summary>
        [JsonPropertyName("configuration")]
        public Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();
    }
}