using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    /// <summary>
    /// Defines the contract for the core engine that processes git changes,
    /// updates statistical models, and calculates risk scores and rankings.
    /// </summary>
    public interface ICoreAnalysisEngine
    {
        /// <summary>
        /// Takes a git delta and a previous analysis state, and returns the new, updated analysis state.
        /// This method encapsulates the entire statistical analysis and risk calculation logic.
        /// </summary>
        /// <param name="delta">The git changes to be processed.</param>
        /// <param name="previousState">The historical state from the last run.</param>
        /// <returns>A new AnalysisState object reflecting the updated metrics and risk rankings.</returns>
        Task<AnalysisState> RunAnalysisAsync(GitDelta delta, AnalysisState previousState);
    }
}