using System.Collections.Generic;
using System.Threading.Tasks;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Defines the contract for the main orchestrator that runs the end-to-end DataLink workflow.
    /// </summary>
    public interface IDataLinkOrchestrator
    {
        /// <summary>
        /// Scans the source code to discover all unique placeholder variables required by [EventSource] attributes.
        /// </summary>
        Task<HashSet<string>> DiscoverRequiredVariablesAsync();

        /// <summary>
        /// Executes the 'generate' command workflow.
        /// </summary>
        Task<int> GenerateAsync();

        /// <summary>
        /// Executes the 'build' command workflow.
        /// </summary>
        Task<int> BuildAsync(string groupName);

        /// <summary>
        /// Executes the 'deploy' command workflow.
        /// </summary>
        Task<int> DeployAsync(string groupName, string artifactPath);

        /// <summary>
        /// A pass-through method to allow the global exception handler to invoke the OnFailure control point.
        /// </summary>
        Task InvokeControlPointFailureAsync(string command, string errorMessage);
    }
}