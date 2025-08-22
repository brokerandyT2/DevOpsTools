using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    /// <summary>
    /// Defines the contract for the service that makes the final PASS/ALERT/FAIL decision
    /// by comparing the previous and current analysis states against configured thresholds.
    /// </summary>
    public interface IDecisionEngine
    {
        /// <summary>
        /// Compares the previous and current analysis states to determine the final outcome.
        /// </summary>
        /// <param name="previousState">The historical analysis state from the last run.</param>
        /// <param name="currentState">The newly calculated analysis state from the current run.</param>
        /// <returns>A RiskAnalysisResult object containing the decision and the reasons for it.</returns>
        Task<RiskAnalysisResult> MakeDecisionAsync(AnalysisState previousState, AnalysisState currentState);
    }
}