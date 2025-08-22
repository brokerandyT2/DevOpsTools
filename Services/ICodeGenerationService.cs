using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for the service that generates the final, buildable source code projects.
    /// </summary>
    public interface ICodeGenerationService
    {
        /// <summary>
        /// Orchestrates the generation of all source code projects for the discovered API groups.
        /// </summary>
        /// <param name="discoveredApis">A JSON document representing the APIs discovered from the business logic.</param>
        /// <param name="manifest">A JSON document representing the deployment manifest.</param>
        /// <returns>A collection of GeneratedProject objects, one for each assembled project.</returns>
        Task<IEnumerable<GeneratedProject>> GenerateProjectsAsync(JsonDocument discoveredApis, JsonDocument manifest);
    }
}