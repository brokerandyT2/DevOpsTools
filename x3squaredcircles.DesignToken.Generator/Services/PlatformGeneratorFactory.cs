using System;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IPlatformGeneratorFactory
    {
        Task<GenerationResult> GenerateAsync(GenerationRequest request, TokensConfiguration config);
    }

    public class PlatformGeneratorFactory : IPlatformGeneratorFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IAppLogger _logger;

        public PlatformGeneratorFactory(IServiceProvider serviceProvider, IAppLogger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, TokensConfiguration config)
        {
            var platform = config.TargetPlatform;
            _logger.LogInfo($"Generating design token files for platform: {platform.ToUpperInvariant()}");

            try
            {
                var generator = GetGeneratorForPlatform(platform);
                return await generator.GenerateAsync(request, config);
            }
            catch (DesignTokenException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError($"Platform generation failed for: {platform}", ex);
                throw new DesignTokenException(DesignTokenExitCode.PlatformGenerationFailure, $"Failed to generate files for {platform}: {ex.Message}", ex);
            }
        }

        private IPlatformGenerator GetGeneratorForPlatform(string platform)
        {
            return platform.ToLowerInvariant() switch
            {
                "android" => (IPlatformGenerator)_serviceProvider.GetService(typeof(IAndroidGeneratorService))!,
                "ios" => (IPlatformGenerator)_serviceProvider.GetService(typeof(IIosGeneratorService))!,
                "web" => (IPlatformGenerator)_serviceProvider.GetService(typeof(IWebGeneratorService))!,
                _ => throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration, $"Unsupported target platform: {platform}")
            };
        }
    }

    /// <summary>
    /// A common interface for all platform-specific generator services.
    /// </summary>
    public interface IPlatformGenerator
    {
        Task<GenerationResult> GenerateAsync(GenerationRequest request, TokensConfiguration config);
    }

    // Specific generator interfaces now inherit from the common one.
    public interface IAndroidGeneratorService : IPlatformGenerator { }
    public interface IIosGeneratorService : IPlatformGenerator { }
    public interface IWebGeneratorService : IPlatformGenerator { }
}