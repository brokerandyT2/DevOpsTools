using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
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
            _logger.LogInformation("Starting code generation for all deployment groups.");

            var groups = manifest.RootElement.GetProperty("groups").EnumerateObject();
            var allApiClasses = discoveredApis.RootElement.GetProperty("apiClasses").EnumerateArray().ToList();

            foreach (var groupProperty in groups)
            {
                var groupName = groupProperty.Name;
                var groupConfig = groupProperty.Value;
                var projectPath = Path.Combine(_config.OutputPath, groupName);
                Directory.CreateDirectory(projectPath);

                var language = groupConfig.TryGetProperty("language", out var langElement) ? langElement.GetString() : _config.Language;
                if (string.IsNullOrEmpty(language))
                {
                    _logger.LogError("Language not defined for group '{Group}' in manifest and no default language is set. Skipping.", groupName);
                    continue;
                }

                var project = new GeneratedProject { GroupName = groupName, OutputPath = projectPath, Language = language };

                var apisForGroup = allApiClasses.Where(api => api.GetProperty("deploymentGroup").GetString() == groupName).ToList();

                if (!apisForGroup.Any())
                {
                    _logger.LogWarning("No API classes found for deployment group '{Group}'. Skipping project generation.", groupName);
                    continue;
                }

                var languageGenerator = _languageGeneratorFactory.Create(language);

                project.SourceFiles = await languageGenerator.GenerateSourceCodeAsync(apisForGroup, projectPath);
                project.ProjectFile = await languageGenerator.GenerateProjectFileAsync(apisForGroup, projectPath, groupConfig);

                generatedProjects.Add(project);
                _logger.LogInformation("✓ Successfully assembled project for group: {Group}", groupName);
            }

            _logger.LogInformation("✓ Code generation complete. Assembled {Count} projects.", generatedProjects.Count);
            return generatedProjects;
        }
    }
}