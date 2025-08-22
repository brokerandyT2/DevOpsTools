using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Weavers;

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
        /// <param name="destinationPath">The root local path where the new project will be created.</param>
        Task WeaveServiceAsync(ServiceBlueprint blueprint, string logicSourcePath, string? testSourcePath, string destinationPath);
    }

    /// <summary>
    /// Implements the logic for weaving together the generated plumbing, the developer's business logic,
    /// and the developer's tests into a single, cohesive, and buildable source code project.
    /// It orchestrates the language- and platform-specific weavers to perform the actual code generation.
    /// </summary>
    public class CodeWeaverService : ICodeWeaverService
    {
        private readonly IAppLogger _logger;
        private readonly LanguageWeaverFactory _weaverFactory;
        private readonly DataLinkConfiguration _config;

        public CodeWeaverService(IAppLogger logger, LanguageWeaverFactory weaverFactory, DataLinkConfiguration config)
        {
            _logger = logger;
            _weaverFactory = weaverFactory;
            _config = config;
        }

        public async Task WeaveServiceAsync(ServiceBlueprint blueprint, string logicSourcePath, string? testSourcePath, string destinationPath)
        {
            _logger.LogStartPhase($"Weaving Service Source Code for: {blueprint.ServiceName}");

            try
            {
                // 1. Get the correct weaver for the specified target language and cloud provider.
                var languageWeaver = _weaverFactory.GetWeaver(_config.TargetLanguage, _config.CloudProvider, blueprint);

                // 2. Set up the destination directory structure.
                // e.g., ./output/{ServiceName}/src
                var mainProjectPath = Path.Combine(destinationPath, blueprint.ServiceName, "src");
                Directory.CreateDirectory(mainProjectPath);

                _logger.LogDebug($"Generating project file for '{blueprint.ServiceName}'...");
                await languageWeaver.GenerateProjectFileAsync(mainProjectPath, logicSourcePath);

                _logger.LogDebug($"Generating startup/DI file for '{blueprint.ServiceName}'...");
                await languageWeaver.GenerateStartupFileAsync(mainProjectPath);

                _logger.LogDebug($"Generating platform-specific files for '{blueprint.ServiceName}'...");
                await languageWeaver.GeneratePlatformFilesAsync(mainProjectPath);

                _logger.LogDebug($"Generating function files for '{blueprint.ServiceName}'...");
                foreach (var triggerMethod in blueprint.TriggerMethods)
                {
                    await languageWeaver.GenerateFunctionFileAsync(triggerMethod, mainProjectPath);
                }

                // 5. Generate and assemble the test harness project, if enabled.
                if (_config.GenerateTestHarness)
                {
                    if (string.IsNullOrEmpty(testSourcePath))
                    {
                        _logger.LogWarning("Generate Test Harness is enabled, but no test harness repository was provided. Skipping test harness generation.");
                    }
                    else
                    {
                        // e.g., ./output/{ServiceName}/tests
                        var testProjectPath = Path.Combine(destinationPath, blueprint.ServiceName, "tests");
                        Directory.CreateDirectory(testProjectPath);
                        await languageWeaver.AssembleTestHarnessAsync(testSourcePath, testProjectPath, mainProjectPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // Wrap any code generation exception in our standard exception type for consistent error handling.
                throw new DataLinkException(ExitCode.CodeGenerationFailed, "WEAVING_FAILED", $"An unexpected error occurred while weaving the service '{blueprint.ServiceName}'.", ex);
            }

            _logger.LogEndPhase($"Weaving Service Source Code for: {blueprint.ServiceName}", true);
        }
    }
}