using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    /// <summary>
    /// Defines the contract for a service that interacts with the Git repository to analyze change history
    /// and manage the tool's persistent state via commits and tags.
    /// </summary>
    public interface IGitAnalysisService
    {
        /// <summary>
        /// Analyzes the repository to find all file changes between the commit marked by the
        /// 'change-analysis-last-run' tag and the current HEAD.
        /// </summary>
        /// <returns>A GitDelta object containing the set of file changes and co-change data.</returns>
        Task<GitDelta> GetDeltaSinceLastRunAsync();

        /// <summary>
        /// Stages and commits the specified analysis file, then forcibly moves the 
        /// 'change-analysis-last-run' tag to this new commit and pushes the changes.
        /// </summary>
        /// <param name="analysisFilePath">The path to the analysis state file to commit (e.g., /src/change-analysis.json).</param>
        Task CommitAnalysisStateAndMoveTagAsync(string analysisFilePath);
    }
}