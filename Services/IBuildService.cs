using System.Threading.Tasks;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Represents the result of a build operation.
    /// </summary>
    /// <param name="Success">Indicates if the build succeeded.</param>
    /// <param name="ArtifactPath">The path to the final, packaged, deployable artifact.</param>
    /// <param name="LogOutput">The captured output from the build process.</param>
    public record BuildResult(bool Success, string ArtifactPath, string LogOutput);

    /// <summary>
    /// Defines the contract for a language-specific build and packaging service.
    /// </summary>
    public interface IBuildService
    {
        /// <summary>
        /// Gets the language this build service supports (e.g., "csharp", "java").
        /// </summary>
        string Language { get; }

        /// <summary>
        /// Compiles and packages the source code in the given project path.
        /// </summary>
        /// <param name="projectPath">The root directory of the generated source project.</param>
        /// <returns>A BuildResult object containing the outcome and artifact path.</returns>
        Task<BuildResult> BuildAsync(string projectPath);
    }
}