using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IPlatformGeneratorFactory
    {
        Task<GenerationResult> GenerateAsync(GenerationRequest request);
    }

    public class PlatformGeneratorFactory : IPlatformGeneratorFactory
    {
        private readonly IAndroidGeneratorService _androidGenerator;
        private readonly IIosGeneratorService _iosGenerator;
        private readonly IWebGeneratorService _webGenerator;
        private readonly ILogger<PlatformGeneratorFactory> _logger;

        public PlatformGeneratorFactory(
            IAndroidGeneratorService androidGenerator,
            IIosGeneratorService iosGenerator,
            IWebGeneratorService webGenerator,
            ILogger<PlatformGeneratorFactory> logger)
        {
            _androidGenerator = androidGenerator;
            _iosGenerator = iosGenerator;
            _webGenerator = webGenerator;
            _logger = logger;
        }

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request)
        {
            var platform = request.Platform.GetSelectedPlatform();

            _logger.LogInformation("Generating design token files for {Platform}", platform.ToUpperInvariant());

            try
            {
                return platform.ToLowerInvariant() switch
                {
                    "android" => await _androidGenerator.GenerateAsync(request),
                    "ios" => await _iosGenerator.GenerateAsync(request),
                    "web" => await _webGenerator.GenerateAsync(request),
                    _ => throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration,
                        $"Unsupported target platform: {platform}")
                };
            }
            catch (DesignTokenException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Platform generation failed for: {Platform}", platform);
                throw new DesignTokenException(DesignTokenExitCode.PlatformGenerationFailure,
                    $"Failed to generate files for {platform}: {ex.Message}", ex);
            }
        }
    }
}