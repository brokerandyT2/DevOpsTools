using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// A factory for creating the appropriate language-specific source code analyzer.
    /// It uses a service provider to resolve the correct implementation at runtime.
    /// </summary>
    public interface ILanguageAnalyzerFactory
    {
        /// <summary>
        /// Gets the analyzer for the specified language by analyzing the contents of a source directory.
        /// </summary>
        /// <param name="sourceDirectory">The directory containing the source code to analyze.</param>
        /// <returns>An instance of ILanguageAnalyzerService.</returns>
        ILanguageAnalyzerService GetAnalyzerForDirectory(string sourceDirectory);
    }

    /// <summary>
    /// Provides an instance of a language-specific analyzer by detecting the project type
    /// within a given source code directory based on common file types and project manifests.
    /// </summary>
    public class LanguageAnalyzerFactory : ILanguageAnalyzerFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IAppLogger _logger;

        public LanguageAnalyzerFactory(IServiceProvider serviceProvider, IAppLogger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public ILanguageAnalyzerService GetAnalyzerForDirectory(string sourceDirectory)
        {
            _logger.LogDebug($"Detecting programming language in directory: {sourceDirectory}");

            // The order of these checks is important. We look for the most specific project
            // files first to avoid ambiguity (e.g., a TypeScript project also contains JS files).

            if (Directory.GetFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories).Any())
            {
                _logger.LogInfo("Detected C# project (.csproj). Using CSharpAnalyzerService.");
                return _serviceProvider.GetRequiredService<CSharpAnalyzerService>();
            }
            if (Directory.GetFiles(sourceDirectory, "pom.xml", SearchOption.AllDirectories).Any() ||
                Directory.GetFiles(sourceDirectory, "build.gradle", SearchOption.AllDirectories).Any())
            {
                _logger.LogInfo("Detected Java project (pom.xml or build.gradle). Using JavaAnalyzerService.");
                return _serviceProvider.GetRequiredService<JavaAnalyzerService>();
            }
            if (Directory.GetFiles(sourceDirectory, "requirements.txt", SearchOption.AllDirectories).Any() ||
                Directory.GetFiles(sourceDirectory, "pyproject.toml", SearchOption.AllDirectories).Any())
            {
                _logger.LogInfo("Detected Python project (requirements.txt or pyproject.toml). Using PythonAnalyzerService.");
                return _serviceProvider.GetRequiredService<PythonAnalyzerService>();
            }
            // Check for TypeScript specifically before JavaScript.
            if (Directory.GetFiles(sourceDirectory, "*.ts", SearchOption.AllDirectories).Any() &&
                Directory.GetFiles(sourceDirectory, "package.json", SearchOption.AllDirectories).Any())
            {
                _logger.LogInfo("Detected TypeScript project (*.ts and package.json). Using JavaScriptAnalyzerService for both.");
                return _serviceProvider.GetRequiredService<JavaScriptAnalyzerService>();
            }
            if (Directory.GetFiles(sourceDirectory, "*.js", SearchOption.AllDirectories).Any() &&
                Directory.GetFiles(sourceDirectory, "package.json", SearchOption.AllDirectories).Any())
            {
                _logger.LogInfo("Detected JavaScript project (*.js and package.json). Using JavaScriptAnalyzerService.");
                return _serviceProvider.GetRequiredService<JavaScriptAnalyzerService>();
            }
            if (Directory.GetFiles(sourceDirectory, "go.mod", SearchOption.AllDirectories).Any())
            {
                _logger.LogInfo("Detected Go project (go.mod). Using GoAnalyzerService.");
                return _serviceProvider.GetRequiredService<GoAnalyzerService>();
            }

            throw new DataLinkException(
                ExitCode.SourceAnalysisFailed,
                "LANGUAGE_DETECTION_FAILED",
                $"Could not determine the programming language for the source code in {sourceDirectory}. No recognized project file (.csproj, pom.xml, package.json, go.mod, etc.) was found.");
        }
    }
}