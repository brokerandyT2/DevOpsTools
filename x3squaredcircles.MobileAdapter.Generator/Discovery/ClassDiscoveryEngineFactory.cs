using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Models;

namespace x3squaredcircles.MobileAdapter.Generator.Discovery
{
    /// <summary>
    /// Defines the contract for a language-specific class discovery engine.
    /// Each implementation is responsible for analyzing source code or assemblies for a given language.
    /// </summary>
    public interface IClassDiscoveryEngine
    {
        Task<List<DiscoveredClass>> DiscoverClassesAsync(GeneratorConfiguration config);
    }

    /// <summary>
    /// Defines the contract for the factory responsible for creating the correct IClassDiscoveryEngine.
    /// </summary>
    public interface IClassDiscoveryEngineFactory
    {
        IClassDiscoveryEngine Create();
    }

    /// <summary>
    /// Factory for creating language-specific class discovery engines based on the application configuration.
    /// It uses the dependency injection container to resolve the correct service.
    /// </summary>
    public class ClassDiscoveryEngineFactory : IClassDiscoveryEngineFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly GeneratorConfiguration _config;
        private readonly ILogger<ClassDiscoveryEngineFactory> _logger;

        public ClassDiscoveryEngineFactory(
            IServiceProvider serviceProvider,
            GeneratorConfiguration config,
            ILogger<ClassDiscoveryEngineFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Creates and returns the appropriate IClassDiscoveryEngine based on the selected source language in the configuration.
        /// </summary>
        /// <returns>An instance of a class that implements IClassDiscoveryEngine.</returns>
        /// <exception cref="MobileAdapterException">Thrown when the configured language is not supported or no language is selected.</exception>
        public IClassDiscoveryEngine Create()
        {
            var selectedLanguage = _config.GetSelectedLanguage();
            _logger.LogDebug("Creating discovery engine for language: {Language}", selectedLanguage);

            switch (selectedLanguage)
            {
                case SourceLanguage.CSharp:
                    return _serviceProvider.GetRequiredService<CSharpDiscoveryEngine>();

                case SourceLanguage.Java:
                    return _serviceProvider.GetRequiredService<JavaDiscoveryEngine>();

                case SourceLanguage.Kotlin:
                    return _serviceProvider.GetRequiredService<KotlinDiscoveryEngine>();

                case SourceLanguage.JavaScript:
                    return _serviceProvider.GetRequiredService<JavaScriptDiscoveryEngine>();

                case SourceLanguage.TypeScript:
                    return _serviceProvider.GetRequiredService<TypeScriptDiscoveryEngine>();

                case SourceLanguage.Python:
                    return _serviceProvider.GetRequiredService<PythonDiscoveryEngine>();

                case SourceLanguage.None:
                    throw new MobileAdapterException(
                        MobileAdapterExitCode.InvalidConfiguration,
                        "No source language was specified in the configuration.");

                default:
                    throw new MobileAdapterException(
                        MobileAdapterExitCode.InvalidConfiguration,
                        $"The source language '{selectedLanguage}' is not supported by the discovery engine factory.");
            }
        }
    }
}