using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    /// <summary>
    /// Defines the contract for a factory that provides the correct language-specific analyzer.
    /// </summary>
    public interface ILanguageAnalyzerFactory
    {
        ILanguageAnalyzer GetAnalyzer(string language);
    }

    /// <summary>
    /// Defines the contract for a language-specific analyzer service.
    /// </summary>
    public interface ILanguageAnalyzer
    {
        Task<List<DiscoveredEntity>> DiscoverEntitiesAsync(string sourcePath, string trackAttribute);
    }

    /// <summary>
    /// Factory that provides a language-specific analyzer based on the application configuration.
    /// It resolves the appropriate analyzer from the dependency injection container.
    /// </summary>
    public class LanguageAnalyzerFactory : ILanguageAnalyzerFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LanguageAnalyzerFactory> _logger;

        public LanguageAnalyzerFactory(IServiceProvider serviceProvider, ILogger<LanguageAnalyzerFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public ILanguageAnalyzer GetAnalyzer(string language)
        {
            _logger.LogDebug("Resolving language analyzer for: {Language}", language);

            Type analyzerType = language.ToLowerInvariant() switch
            {
                "csharp" => typeof(CSharpAnalyzerService),
                "java" => typeof(JavaAnalyzerService),
                "python" => typeof(PythonAnalyzerService),
                "javascript" => typeof(JavaScriptAnalyzerService),
                "typescript" => typeof(TypeScriptAnalyzerService),
                "go" => typeof(GoAnalyzerService),
                _ => throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration, $"Unsupported language: {language}")
            };

            var analyzer = (ILanguageAnalyzer)_serviceProvider.GetService(analyzerType);

            if (analyzer == null)
            {
                throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration, $"Could not resolve language analyzer for '{language}'. Ensure it is registered in Program.cs.");
            }

            return analyzer;
        }
    }
}