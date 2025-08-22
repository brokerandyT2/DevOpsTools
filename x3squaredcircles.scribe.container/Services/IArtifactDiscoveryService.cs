using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Models.Artifacts;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for a service that discovers 3SC tool artifacts
    /// and the native pipeline definition file within the CI/CD workspace.
    /// </summary>
    public interface IArtifactDiscoveryService
    {
        /// <summary>
        /// Scans the specified workspace path for 3SC tool artifacts and the pipeline file.
        /// </summary>
        /// <remarks>
        /// The discovery process is convention-based. It identifies files in the root of the
        /// workspace, separating the known pipeline file from other potential evidence files.
        /// </remarks>
        /// <param name="workspacePath">The root path of the CI/CD workspace to scan.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result is a
        /// <see cref="DiscoveryResult"/> object containing both the collection of
        /// generic artifacts and the path to the identified pipeline file.
        /// </returns>
        Task<DiscoveryResult> DiscoverArtifactsAsync(string workspacePath);
    }
}