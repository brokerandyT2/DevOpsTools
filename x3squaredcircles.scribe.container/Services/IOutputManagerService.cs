using System.Threading.Tasks;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for managing the Scribe's output directory structure.
    /// This service is responsible for creating the final, hierarchically named root directory
    /// as per the architectural specification.
    /// </summary>
    public interface IOutputManagerService
    {
        /// <summary>
        /// Gets the fully resolved, absolute path to the final release directory.
        /// This path is determined only after InitializeOutputStructureAsync has been called.
        /// Example: /src/wiki/RELEASES/MyWebApp/1.5.0-20231027/
        /// </summary>
        string FinalReleasePath { get; }

        /// <summary>
        /// Gets the fully resolved, absolute path to the 'attachments' subdirectory within the final release directory.
        /// This path is determined only after InitializeOutputStructureAsync has been called.
        /// Example: /src/wiki/RELEASES/MyWebApp/1.5.0-20231027/attachments/
        /// </summary>
        string AttachmentsPath { get; }

        /// <summary>
        /// Creates the entire standardized directory structure based on the application configuration.
        /// This method is idempotent; it will not fail if the directory already exists.
        /// </summary>
        /// <param name="releaseVersion">The version of the release (e.g., "1.5.0").</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task InitializeOutputStructureAsync(string releaseVersion);
    }
}