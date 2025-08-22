using System.Threading.Tasks;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    /// <summary>
    /// Defines the contract for the main orchestrator that manages the execution
    /// of the configured gate mode (`Basic`, `Advanced`, or `Custom`).
    /// </summary>
    public interface IPipelineGateOrchestrator
    {
        /// <summary>
        /// Executes the end-to-end pipeline gate logic based on the loaded configuration.
        /// </summary>
        /// <returns>The final exit code for the application.</returns>
        Task<int> RunAsync();
    }
}