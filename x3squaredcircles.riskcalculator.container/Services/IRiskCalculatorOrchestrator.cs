using System.Threading.Tasks;

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    /// <summary>
    /// Defines the contract for the main orchestrator that coordinates the
    /// entire risk calculation workflow, from Git analysis to the final decision.
    /// </summary>
    public interface IRiskCalculatorOrchestrator
    {
        /// <summary>
        /// Executes the end-to-end risk calculation process.
        /// </summary>
        /// <returns>The final exit code for the application.</returns>
        Task<int> RunAsync();
    }
}