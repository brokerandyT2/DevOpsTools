using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for a factory that creates language-specific code generators.
    /// </summary>
    public interface ILanguageGeneratorFactory
    {
        /// <summary>
        /// Creates an instance of a language-specific generator.
        /// </summary>
        /// <param name="language">The name of the language (e.g., "csharp", "java").</param>
        /// <returns>An instance of ILanguageGenerator.</returns>
        /// <exception cref="AssemblerException">Thrown if the requested language is not supported.</exception>
        ILanguageGenerator Create(string language);
    }

    /// <summary>
    /// Factory for creating language-specific code generators based on configuration.
    /// It uses the dependency injection container to resolve the correct service implementation.
    /// </summary>
    public class LanguageGeneratorFactory : ILanguageGeneratorFactory
    {
        private readonly IServiceProvider _serviceProvider;

        // A single source of truth for supported languages.
        private static readonly string[] SupportedLanguages = { "csharp", "java", "python", "typescript", "go", "javascript" };

        public LanguageGeneratorFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ILanguageGenerator Create(string language)
        {
            var languageKey = language.ToLowerInvariant();

            // Explicitly check for supported languages to provide a better error message.
            if (!SupportedLanguages.Contains(languageKey))
            {
                var supported = string.Join(", ", SupportedLanguages);
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Code generation for language '{language}' is not supported. Supported languages are: {supported}.");
            }

            try
            {
                // The switch expression provides a clean way to map the key to the service type.
                return languageKey switch
                {
                    "csharp" => _serviceProvider.GetRequiredService<CSharpGenerator>(),
                    "java" => _serviceProvider.GetRequiredService<JavaGenerator>(),
                    "python" => _serviceProvider.GetRequiredService<PythonGenerator>(),
                    "typescript" => _serviceProvider.GetRequiredService<TypeScriptGenerator>(),
                    "go" => _serviceProvider.GetRequiredService<GoGenerator>(),
                    "javascript" => _serviceProvider.GetRequiredService<JavaScriptGenerator>(),
                    // This default case should be unreachable due to the check above, but is good practice.
                    _ => throw new InvalidOperationException($"Internal error: No generator registered for language '{languageKey}'.")
                };
            }
            catch (Exception ex)
            {
                // This will catch errors if a generator was not correctly registered in Program.cs.
                throw new AssemblerException(AssemblerExitCode.UnhandledException, $"Failed to create language generator for '{language}'. Check application startup configuration.", ex);
            }
        }
    }
}