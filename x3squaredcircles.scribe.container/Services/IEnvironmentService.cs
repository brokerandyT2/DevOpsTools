namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for a service that abstracts and provides access to
    /// the CI/CD pipeline's runtime environment variables. This service is the
    /// Scribe's primary mechanism for being "environment-aware".
    /// </summary>
    public interface IEnvironmentService
    {
        /// <summary>
        /// Gets the absolute path to the root of the CI/CD workspace.
        /// This is the primary directory where all artifacts and source code are located.
        /// </summary>
        string GetWorkspacePath();

        /// <summary>
        /// Gets the release version or build number for the current pipeline run.
        /// </summary>
        string GetReleaseVersion();

        /// <summary>
        /// Gets the unique identifier for the current pipeline run.
        /// </summary>
        string GetPipelineRunId();

        /// <summary>
        /// Gets the source Git commit or commit range for the build.
        /// </summary>
        string GetGitRange();
    }
}