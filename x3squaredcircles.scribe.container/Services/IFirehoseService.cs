using x3squaredcircles.scribe.container.Models.Firehose;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for a service that serializes the final release artifact data
    /// to a JSON string and writes it to the primary log stream for machine consumption.
    /// </summary>
    public interface IFirehoseService
    {
        /// <summary>
        /// Takes the complete release artifact data model, serializes it to JSON,
        /// and writes it to the configured logger at an Information level.
        /// </summary>
        /// <param name="artifactData">The root data model of the release artifact.</param>
        void LogArtifact(ReleaseArtifactData artifactData);
    }
}