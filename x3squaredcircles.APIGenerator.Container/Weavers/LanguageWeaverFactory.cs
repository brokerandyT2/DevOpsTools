using System;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;
using x3squaredcircles.DataLink.Container.Weavers;

namespace x3squaredcircles.datalink.container.Weavers
{
    /// <summary>
    /// Defines the contract for a language-specific code generation helper. An implementation
    /// of this interface is an "expert" on generating a buildable project for one specific
    /// language and platform combination (e.g., C# on AWS Lambda).
    /// </summary>
    public interface ILanguageWeaver
    {
        /// <summary>
        /// Generates the primary project file (e.g., .csproj, pom.xml, package.json).
        /// </summary>
        Task GenerateProjectFileAsync(string projectPath, string logicSourcePath);

        /// <summary>
        /// Generates the startup/DI configuration file (e.g., Program.cs, main.go).
        /// </summary>
        Task GenerateStartupFileAsync(string projectPath);

        /// <summary>
        /// Generates platform-specific configuration files (e.g., host.json, template.yaml, cloudbuild.yaml).
        /// </summary>
        Task GeneratePlatformFilesAsync(string projectPath);

        /// <summary>
        /// Generates a single source file representing a function entry point for a trigger.
        /// </summary>
        Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath);

        /// <summary>
        /// Assembles a complete, buildable test harness project.
        /// </summary>
        Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath);
    }

    /// <summary>
    /// A factory that provides the correct ILanguageWeaver implementation for a given
    /// target language and target platform combination. It acts as the central router
    /// for all code generation tasks.
    /// </summary>
    public class LanguageWeaverFactory
    {
        private readonly IAppLogger _logger;

        public LanguageWeaverFactory(IAppLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets the specific weaver required for the target language and platform matrix.
        /// </summary>
        /// <param name="targetLanguage">The language for the generated source code (e.g., "csharp").</param>
        /// <param name="targetPlatform">The deployment platform for the generated shim (e.g., "azure", "aws", "gcp").</param>
        /// <param name="blueprint">The language-agnostic blueprint for the service to be generated.</param>
        /// <returns>A concrete implementation of ILanguageWeaver.</returns>
        public ILanguageWeaver GetWeaver(string targetLanguage, string targetPlatform, ServiceBlueprint blueprint)
        {
            _logger.LogInfo($"Selecting weaver for Target Language: '{targetLanguage}', Target Platform: '{targetPlatform}'");

            // This switch expression implements the definitive "Weaver Matrix".
            // It maps a specific (language, platform) tuple to its concrete weaver implementation.
            return (targetLanguage.ToLowerInvariant(), targetPlatform.ToLowerInvariant()) switch
            {
                // --- C# Weavers ---
                ("csharp", "azure") => new CSharpAzureFunctionsWeaver(_logger, blueprint),
                ("csharp", "aws") => new CSharpAwsLambdaWeaver(_logger, blueprint),
               // ("csharp", "oracle") => new CSharpOciFunctionsWeaver(_logger, blueprint),
                ("csharp", "gcp") => new CSharpGcpFunctionsWeaver(_logger, blueprint), // To be implemented

                // --- Java Weavers ---
                ("java", "azure") => new JavaAzureFunctionsWeaver(_logger, blueprint),
                ("java", "aws") => new JavaAwsLambdaWeaver(_logger, blueprint),
               // ("java", "oracle") => new JavaOciFunctionsWeaver(_logger, blueprint),
                ("java", "gcp") => new JavaGcpFunctionsWeaver(_logger, blueprint), // To be implemented

                // --- Python Weavers ---
                ("python", "azure") => new PythonAzureFunctionsWeaver(_logger, blueprint),
                ("python", "aws") => new PythonAwsLambdaWeaver(_logger, blueprint),
               // ("python", "oracle") => new PythonOciFunctionsWeaver(_logger, blueprint),
                ("python", "gcp") => new PythonGcpFunctionsWeaver(_logger, blueprint), // To be implemented

                // --- TypeScript Weavers ---
                ("typescript", "azure") => new TypeScriptAzureFunctionsWeaver(_logger, blueprint),
                ("typescript", "aws") => new TypeScriptAwsLambdaWeaver(_logger, blueprint),
               // ("typescript", "oracle") => new TypeScriptOciFunctionsWeaver(_logger, blueprint),
                ("typescript", "gcp") => new TypeScriptGcpFunctionsWeaver(_logger, blueprint), // To be implemented

                // --- JavaScript Weavers ---
                ("javascript", "azure") => new JavaScriptAzureFunctionsWeaver(_logger, blueprint),
                ("javascript", "aws") => new JavaScriptAwsLambdaWeaver(_logger, blueprint),
              //  ("javascript", "oracle") => new JavaScriptOciFunctionsWeaver(_logger, blueprint),
                ("javascript", "gcp") => new JavaScriptGcpFunctionsWeaver(_logger, blueprint), // To be implemented

                // --- Go Weavers ---
                ("go", "azure") => new GoAzureFunctionsWeaver(_logger, blueprint),
                ("go", "aws") => new GoAwsLambdaWeaver(_logger, blueprint),
              //  ("go", "oracle") => new GoOciFunctionsWeaver(_logger, blueprint),
                ("go", "gcp") => new GoGcpFunctionsWeaver(_logger, blueprint), // To be implemented

                // --- Default Case ---
                _ => throw new DataLinkException(
                    ExitCode.CodeGenerationFailed,
                    "UNSUPPORTED_TARGET_COMBINATION",
                    $"The combination of target language '{targetLanguage}' and platform '{targetPlatform}' is not a supported generation target.")
            };
        }
    }
}