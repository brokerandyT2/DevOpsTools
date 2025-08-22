using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    /// <summary>
    /// Defines the contract for a service that formats the final risk analysis result
    /// into a human-readable format for console output.
    /// </summary>
    public interface IOutputFormatter
    {
        /// <summary>
        /// Generates and logs the final output to the console based on the analysis result and logging verbosity.
        /// </summary>
        /// <param name="result">The final result from the Decision Engine.</param>
        Task LogFinalOutputAsync(RiskAnalysisResult result);
    }
}