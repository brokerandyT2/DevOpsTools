using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    public class CodeGenerationService : ICodeGenerationService
    {
        private readonly ILogger<CodeGenerationService> _logger;
        private readonly AssemblerConfiguration _config;
        private readonly ILanguageGeneratorFactory _languageGeneratorFactory;

        public CodeGenerationService(ILogger<CodeGenerationService> logger, AssemblerConfiguration config, ILanguageGeneratorFactory languageGeneratorFactory)
        {
            _logger = logger;
            _config = config;
            _languageGeneratorFactory = languageGeneratorFactory;
        }

        public async Task<IEnumerable<GeneratedProject>> GenerateProjectsAsync(JsonDocument discoveredApis, JsonDocument manifest)
        {
            var generatedProjects = new List<GeneratedProject>();
            _logger.LogInformation("--- Starting code generation for all deployment groups ---");

            if (!manifest.RootElement.TryGetProperty("groups", out var groupsElement) || groupsElement.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("Manifest does not contain a valid 'groups' object. No projects will be generated.");
                return generatedProjects;
            }

            if (!discoveredApis.RootElement.TryGetProperty("apiClasses", out var apiClassesElement) || apiClassesElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Discovered APIs document does not contain a valid 'apiClasses' array. No projects will be generated.");
                return generatedProjects;
            }

            var allApiClasses = apiClassesElement.EnumerateArray().ToList();

            foreach (var groupProperty in groupsElement.EnumerateObject())
            {
                var groupName = groupProperty.Name;
                var groupConfig = groupProperty.Value;
                var projectPath = Path.Combine(_config.OutputPath, groupName);

                try
                {
                    Directory.CreateDirectory(projectPath);

                    var language = groupConfig.TryGetProperty("language", out var langElement) ? langElement.GetString() : _config.Language;
                    if (string.IsNullOrEmpty(language))
                    {
                        _logger.LogError("Language not defined for group '{Group}' in manifest and no default language is set (3SC_LANGUAGE). Skipping.", groupName);
                        continue;
                    }

                    var apisForGroup = allApiClasses
                        .Where(api => api.TryGetProperty("deploymentGroup", out var group) && group.GetString() == groupName)
                        .ToList();

                    if (!apisForGroup.Any())
                    {
                        _logger.LogWarning("No API classes were discovered for deployment group '{Group}'. Skipping project generation for this group.", groupName);
                        continue;
                    }

                    _logger.LogInformation("Assembling project for group '{Group}' using language '{Language}' into: {Path}", groupName, language, projectPath);

                    var project = new GeneratedProject { GroupName = groupName, OutputPath = projectPath, Language = language };
                    var languageGenerator = _languageGeneratorFactory.Create(language);

                    project.SourceFiles = await languageGenerator.GenerateSourceCodeAsync(apisForGroup, projectPath);
                    project.ProjectFile = await languageGenerator.GenerateProjectFileAsync(apisForGroup, projectPath, groupConfig);

                    generatedProjects.Add(project);
                    _logger.LogInformation("✓ Successfully assembled project for group: {Group}", groupName);
                }
                catch (AssemblerException)
                {
                    // Re-throw our specific exceptions to halt the process.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An unexpected error occurred while generating project for group '{Group}'.", groupName);
                    // Wrap the exception to provide context and fail the entire run.
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate project for group '{groupName}'.", ex);
                }
            }

            _logger.LogInformation("✓ Code generation complete. Assembled {Count} projects.", generatedProjects.Count);
            return generatedProjects;
        }
    }
}