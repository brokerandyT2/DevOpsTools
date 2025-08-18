using System;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;

namespace x3squaredcircles.datalink.container.Weavers
{
    /// <summary>
    // Defines the contract for a language-specific code generation helper.
    /// </summary>
    public interface ILanguageWeaver
    {
        Task GenerateProjectFileAsync(string projectPath, string logicSourcePath);
        Task GenerateStartupFileAsync(string projectPath);
        Task GeneratePlatformFilesAsync(string projectPath);
        Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath);
        Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath);
    }

    /// <summary>
    /// A factory that provides the correct ILanguageWeaver implementation for a given
    // target language and target platform combination.
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
        /// <param name="targetPlatform">The deployment platform for the generated shim (e.g., "azure-functions").</param>
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
                ("csharp", "azure-functions") => new CSharpAzureFunctionsWeaver(_logger, blueprint),
                ("csharp", "aws-lambda") => new CSharpAwsLambdaWeaver(_logger, blueprint),
                ("csharp", "oci-functions") => new CSharpOciFunctionsWeaver(_logger, blueprint),

                // --- Java Weavers ---
                ("java", "azure-functions") => new JavaAzureFunctionsWeaver(_logger, blueprint),
                ("java", "aws-lambda") => new JavaAwsLambdaWeaver(_logger, blueprint),
                ("java", "oci-functions") => new JavaOciFunctionsWeaver(_logger, blueprint),

                // --- Python Weavers ---
                ("python", "azure-functions") => new PythonAzureFunctionsWeaver(_logger, blueprint),
                ("python", "aws-lambda") => new PythonAwsLambdaWeaver(_logger, blueprint),
                ("python", "oci-functions") => new PythonOciFunctionsWeaver(_logger, blueprint),

                // --- TypeScript Weavers ---
                ("typescript", "azure-functions") => new TypeScriptAzureFunctionsWeaver(_logger, blueprint),
                ("typescript", "aws-lambda") => new TypeScriptAwsLambdaWeaver(_logger, blueprint),
                ("typescript", "oci-functions") => new TypeScriptOciFunctionsWeaver(_logger, blueprint),

                // --- JavaScript Weavers ---
                ("javascript", "azure-functions") => new JavaScriptAzureFunctionsWeaver(_logger, blueprint),
                ("javascript", "aws-lambda") => new JavaScriptAwsLambdaWeaver(_logger, blueprint),
                ("javascript", "oci-functions") => new JavaScriptOciFunctionsWeaver(_logger, blueprint),

                // --- Go Weavers ---
                ("go", "azure-functions") => new GoAzureFunctionsWeaver(_logger, blueprint),
                ("go", "aws-lambda") => new GoAwsLambdaWeaver(_logger, blueprint),
                ("go", "oci-functions") => new GoOciFunctionsWeaver(_logger, blueprint),

                // --- Default Case ---
                _ => throw new DataLinkException(
                    ExitCode.CodeGenerationFailed,
                    "UNSUPPORTED_TARGET_COMBINATION",
                    $"The combination of target language '{targetLanguage}' and platform '{targetPlatform}' is not a supported generation target.")
            };
        }
    }
}