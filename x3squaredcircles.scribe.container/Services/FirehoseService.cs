using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using x3squaredcircles.scribe.container.Models.Firehose;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements the logic for serializing the final release artifact data to JSON
    /// and writing it to the primary log stream.
    /// </summary>
    public class FirehoseService : IFirehoseService
    {
        private readonly ILogger<FirehoseService> _logger;
        private readonly JsonSerializerOptions _jsonSerializerOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="FirehoseService"/> class.
        /// </summary>
        /// <param name="logger">The logger for writing the firehose output.</param>
        public FirehoseService(ILogger<FirehoseService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Configure the JSON serializer for clean, readable output.
            _jsonSerializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true, // Makes the JSON output human-readable in logs.
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // Omits null properties.
            };
        }

        /// <inheritdoc />
        public void LogArtifact(ReleaseArtifactData artifactData)
        {
            if (artifactData == null)
            {
                _logger.LogError("Cannot log artifact to firehose because the provided artifact data is null.");
                return;
            }

            try
            {
                var jsonArtifact = JsonSerializer.Serialize(artifactData, _jsonSerializerOptions);

                // This is the core firehose logging action. We log the entire JSON object
                // as a single, structured message at the Information level.
                // The "FIREHOSE:" prefix makes it easily searchable in log streams.
                _logger.LogInformation("FIREHOSE: Final Release Artifact Data:\n{JsonArtifact}", jsonArtifact);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "A critical error occurred while serializing the artifact data for the firehose log. The final JSON output will be missing.");
            }
        }
    }
}