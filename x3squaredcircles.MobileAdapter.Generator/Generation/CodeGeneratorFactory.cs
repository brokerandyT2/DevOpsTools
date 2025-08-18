using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Discovery;
using x3squaredcircles.MobileAdapter.Generator.Models;
using x3squaredcircles.MobileAdapter.Generator.TypeMapping;

namespace x3squaredcircles.MobileAdapter.Generator.Generation
{
    /// <summary>
    /// Defines the contract for a platform-specific code generator.
    /// Each implementation is responsible for creating adapter files for a target platform (e.g., Android, iOS).
    /// </summary>
    public interface ICodeGenerator
    {
        Task<List<string>> GenerateAdaptersAsync(
            List<DiscoveredClass> discoveredClasses,
            Dictionary<string, TypeMappingInfo> typeMappings,
            GeneratorConfiguration config);
    }

    /// <summary>
    /// Defines the contract for the factory responsible for creating the correct ICodeGenerator.
    /// </summary>
    public interface ICodeGeneratorFactory
    {
        ICodeGenerator Create();
    }

    /// <summary>
    /// Factory for creating platform-specific code generators based on the application configuration.
    /// It uses the dependency injection container to resolve the correct service.
    /// </summary>
    public class CodeGeneratorFactory : ICodeGeneratorFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly GeneratorConfiguration _config;
        private readonly ILogger<CodeGeneratorFactory> _logger;

        public CodeGeneratorFactory(
            IServiceProvider serviceProvider,
            GeneratorConfiguration config,
            ILogger<CodeGeneratorFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Creates and returns the appropriate ICodeGenerator based on the selected target platform in the configuration.
        /// </summary>
        /// <returns>An instance of a class that implements ICodeGenerator.</returns>
        /// <exception cref="MobileAdapterException">Thrown when the configured platform is not supported or no platform is selected.</exception>
        public ICodeGenerator Create()
        {
            var selectedPlatform = _config.GetSelectedPlatform();
            _logger.LogDebug("Creating code generator for platform: {Platform}", selectedPlatform);

            switch (selectedPlatform)
            {
                case TargetPlatform.Android:
                    return _serviceProvider.GetRequiredService<AndroidCodeGenerator>();

                case TargetPlatform.iOS:
                    return _serviceProvider.GetRequiredService<IosCodeGenerator>();

                case TargetPlatform.None:
                    throw new MobileAdapterException(
                        MobileAdapterExitCode.InvalidConfiguration,
                        "No target platform was specified in the configuration.");

                default:
                    throw new MobileAdapterException(
                        MobileAdapterExitCode.InvalidConfiguration,
                        $"The target platform '{selectedPlatform}' is not supported by the code generator factory.");
            }
        }
    }
}