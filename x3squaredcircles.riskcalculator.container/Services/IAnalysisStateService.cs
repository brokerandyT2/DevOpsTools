using System.Threading.Tasks;
using x3squaredcircles.RiskCalculator.Container.Models;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    /// <summary>
    /// Defines the contract for a service that manages the loading and saving
    /// of the tool's persistent state file (change-analysis.json).
    /// </summary>
    public interface IAnalysisStateService
    {
        /// <summary>
        /// Loads the analysis state from the 'change-analysis.json' file in the repository root.
        /// </summary>
        /// <returns>The deserialized AnalysisState object, or a new empty state if the file does not exist.</returns>
        Task<AnalysisState> LoadStateAsync();

        /// <summary>
        /// Serializes the provided AnalysisState object to JSON and saves it to 'change-analysis.json'.
        /// </summary>
        /// <param name="state">The analysis state to save.</param>
        /// <returns>The full path to the saved file.</returns>
        Task<string> SaveStateAsync(AnalysisState state);
    }
}