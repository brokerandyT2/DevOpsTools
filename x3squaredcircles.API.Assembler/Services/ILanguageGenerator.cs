using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for a language-specific code generator.
    /// </summary>
    public interface ILanguageGenerator
    {
        /// <summary>
        /// Generates all necessary source code files for a given set of API definitions.
        /// </summary>
        /// <param name="apisForGroup">The discovered API classes for a single deployment group.</param>
        /// <param name="projectPath">The root directory for the generated project.</param>
        /// <returns>A list of paths to the generated source files.</returns>
        Task<List<string>> GenerateSourceCodeAsync(List<JsonElement> apisForGroup, string projectPath);

        /// <summary>
        /// Generates the primary project file (e.g., .csproj, pom.xml) for the generated source code.
        /// </summary>
        /// <param name="apisForGroup">The discovered API classes for the group.</param>
        /// <param name="projectPath">The root directory for the generated project.</param>
        /// <param name="groupConfig">The configuration for the deployment group from the manifest.</param>
        /// <returns>The path to the generated project file.</returns>
        Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig);
    }
}