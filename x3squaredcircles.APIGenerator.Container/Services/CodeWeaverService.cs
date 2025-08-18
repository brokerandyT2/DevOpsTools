using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Weavers;
using x3squaredcircles.DataLink.Container.Weavers;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Defines the contract for the service that generates the final shim source code.
    /// </summary>
    public interface ICodeWeaverService
    {
        /// <summary>
        /// Generates a complete, buildable source code project for a service shim and its associated test harness.
        /// </summary>
        /// <param name="blueprint">The language-agnostic blueprint of the service to build.</param>
        /// <param name="logicSourcePath">The local path to the developer's business logic source code project.</param>
        /// <param name="testSourcePath">The optional local path to the developer's test harness source code.</param>
        /// <param name="destinationPath">The local path where the new project will be created.</param>
        Task WeaveServiceAsync(ServiceBlueprint blueprint, string logicSourcePath, string? testSourcePath, string destinationPath);
    }

    /// <summary>
    /// Implements the logic for weaving together the generated plumbing, the developer's business logic,
    /// and the developer's tests into a single, cohesive, and buildable source code project.
    /// </summary>
    public class CodeWeaverService : ICodeWeaverService
    {
        private readonly IAppLogger _logger;
        private readonly LanguageWeaverFactory _weaverFactory;
        private readonly DataLinkConfiguration _config;

        public CodeWeaverService(IAppLogger logger, LanguageWeaverFactory weaverFactory, IConfigurationService configService)
        {
            _logger = logger;
            _weaverFactory = weaverFactory;
            _config = configService.GetConfiguration();
        }

        public async Task WeaveServiceAsync(ServiceBlueprint blueprint, string logicSourcePath, string? testSourcePath, string destinationPath)
        {
            _logger.LogStartPhase($"Weaving Service Source Code: {blueprint.ServiceName}");

            try
            {
                var mainProjectPath = Path.Combine(destinationPath, "src", blueprint.ServiceName);
                Directory.CreateDirectory(mainProjectPath);

                // This logic would be expanded to select the appropriate weaver based on a --target-language flag.
                // For now, we default to the blueprint's source language.
                var targetLanguage = "csharp";
                var languageWeaver = _weaverFactory.GetWeaver(targetLanguage, blueprint);

                // 1. Generate language-specific project file(s) (e.g., .csproj)
                await languageWeaver.GenerateProjectFileAsync(mainProjectPath, logicSourcePath);

                // 2. Generate the dependency injection and startup file (e.g., Program.cs)
                await languageWeaver.GenerateStartupFileAsync(mainProjectPath);

                // 3. Generate supporting platform files (e.g., host.json)
                await languageWeaver.GeneratePlatformFilesAsync(mainProjectPath);

                // 4. Generate a separate, complete function file for EACH trigger method
                foreach (var triggerMethod in blueprint.TriggerMethods)
                {
                    await languageWeaver.GenerateFunctionFileAsync(triggerMethod, mainProjectPath);
                }

                // 5. Generate and assemble the test harness project, if enabled and available.
                if (_config.GenerateTestHarness && !string.IsNullOrEmpty(testSourcePath))
                {
                    var testProjectPath = Path.Combine(destinationPath, "tests", $"{blueprint.ServiceName}.Tests");
                    Directory.CreateDirectory(testProjectPath);
                    await languageWeaver.AssembleTestHarnessAsync(testSourcePath, testProjectPath, mainProjectPath);
                }

                _logger.LogStructuredEvent("WeavingComplete", new { ServiceName = blueprint.ServiceName, TargetLanguage = targetLanguage });
            }
            catch (Exception ex)
            {
                throw new DataLinkException(ExitCode.CodeGenerationFailed, "WEAVING_FAILED", $"Failed to weave the service '{blueprint.ServiceName}'.", ex);
            }

            _logger.LogEndPhase($"Weaving Service Source Code: {blueprint.ServiceName}", true);
        }
    }

    /// <summary>
    /// A factory that provides the correct ILanguageWeaver implementation for a given target language.
    /// </summary>
    public class LanguageWeaverFactory
    {
        private readonly IAppLogger _logger;

        public LanguageWeaverFactory(IAppLogger logger)
        {
            _logger = logger;
        }

        public ILanguageWeaver GetWeaver(string targetLanguage, ServiceBlueprint blueprint)
        {
            return targetLanguage.ToLowerInvariant() switch
            {
                "csharp" => new CSharpWeaver(_logger, blueprint),
                _ => throw new DataLinkException(
                    ExitCode.CodeGenerationFailed,
                    "UNSUPPORTED_TARGET_LANGUAGE",
                    $"The target language '{targetLanguage}' is not supported for code generation.")
            };
        }
    }





}