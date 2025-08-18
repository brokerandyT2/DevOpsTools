using Microsoft.Extensions.DependencyInjection;
using System;
using x3squaredcircles.API.Assembler.Models;


namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for a factory that creates language-specific code generators.
    /// </summary>
    public interface ILanguageGeneratorFactory
    {
        ILanguageGenerator Create(string language);
    }

    /// <summary>
    /// Factory for creating language-specific code generators based on configuration.
    /// It uses the dependency injection container to resolve the correct service.
    /// </summary>
    public class LanguageGeneratorFactory : ILanguageGeneratorFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public LanguageGeneratorFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ILanguageGenerator Create(string language)
        {
            return language.ToLowerInvariant() switch
            {
                "csharp" => (ILanguageGenerator)_serviceProvider.GetRequiredService(typeof(CSharpGenerator)),
                "java" => (ILanguageGenerator)_serviceProvider.GetRequiredService(typeof(JavaGenerator)),
                "python" => (ILanguageGenerator)_serviceProvider.GetRequiredService(typeof(PythonGenerator)),
                "typescript" => (ILanguageGenerator)_serviceProvider.GetRequiredService(typeof(TypeScriptGenerator)),
                "go" => (ILanguageGenerator)_serviceProvider.GetRequiredService(typeof(GoGenerator)),
                "javascript" => (ILanguageGenerator)_serviceProvider.GetRequiredService(typeof(JavaScriptGenerator)),
                _ => throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, $"Code generation for language '{language}' is not supported."),
            };
        }
    }
}