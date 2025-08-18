using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface ILanguageAnalyzerFactory
    {
        Task<ILanguageAnalyzer> GetAnalyzerAsync(string language);
    }

    public interface ILanguageAnalyzer
    {
        Task<List<DiscoveredEntity>> DiscoverEntitiesAsync(string sourcePath, string trackAttribute);
    }

    public class LanguageAnalyzerFactory : ILanguageAnalyzerFactory
    {
        private readonly ICSharpAnalyzerService _csharpAnalyzer;
        private readonly IJavaAnalyzerService _javaAnalyzer;
        private readonly IPythonAnalyzerService _pythonAnalyzer;
        private readonly IJavaScriptAnalyzerService _javascriptAnalyzer;
        private readonly ITypeScriptAnalyzerService _typescriptAnalyzer;
        private readonly IGoAnalyzerService _goAnalyzer;
        private readonly ILogger<LanguageAnalyzerFactory> _logger;

        public LanguageAnalyzerFactory(
            ICSharpAnalyzerService csharpAnalyzer,
            IJavaAnalyzerService javaAnalyzer,
            IPythonAnalyzerService pythonAnalyzer,
            IJavaScriptAnalyzerService javascriptAnalyzer,
            ITypeScriptAnalyzerService typescriptAnalyzer,
            IGoAnalyzerService goAnalyzer,
            ILogger<LanguageAnalyzerFactory> logger)
        {
            _csharpAnalyzer = csharpAnalyzer;
            _javaAnalyzer = javaAnalyzer;
            _pythonAnalyzer = pythonAnalyzer;
            _javascriptAnalyzer = javascriptAnalyzer;
            _typescriptAnalyzer = typescriptAnalyzer;
            _goAnalyzer = goAnalyzer;
            _logger = logger;
        }

        public async Task<ILanguageAnalyzer> GetAnalyzerAsync(string language)
        {
            _logger.LogDebug("Getting language analyzer for: {Language}", language);

            return language.ToLowerInvariant() switch
            {
                "csharp" => _csharpAnalyzer,
                "java" => _javaAnalyzer,
                "python" => _pythonAnalyzer,
                "javascript" => _javascriptAnalyzer,
                "typescript" => _typescriptAnalyzer,
                "go" => _goAnalyzer,
                _ => throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration,
                    $"Unsupported language: {language}")
            };
        }
    }

    // C# Analyzer Service
    

    

}