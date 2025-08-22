using System;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IDesignPlatformFactory
    {
        Task<TokenCollection> ExtractTokensAsync(TokensConfiguration config);
    }

    public class DesignPlatformFactory : IDesignPlatformFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IAppLogger _logger;

        public DesignPlatformFactory(IServiceProvider serviceProvider, IAppLogger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractTokensAsync(TokensConfiguration config)
        {
            var platform = config.DesignPlatform;
            _logger.LogInfo($"Extracting design tokens from {platform.ToUpperInvariant()}...");

            try
            {
                var connector = GetConnectorForPlatform(platform);
                return await connector.ExtractTokensAsync(config);
            }
            catch (DesignTokenException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError($"Token extraction failed for platform: {platform}", ex);
                throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, $"Failed to extract tokens from {platform}: {ex.Message}", ex);
            }
        }

        private IDesignPlatformConnector GetConnectorForPlatform(string platform)
        {
            // This is the definitive mapping of the platform string to the concrete service type.
            // The service provider is responsible for creating an instance of the correct class.
            Type? connectorType = platform.ToLowerInvariant() switch
            {
                "figma" => typeof(IFigmaConnectorService),
                "sketch" => typeof(ISketchConnectorService),
                "adobe-xd" => typeof(IAdobeXdConnectorService),
                "zeplin" => typeof(IZeplinConnectorService),
                "abstract" => typeof(IAbstractConnectorService),
                "penpot" => typeof(IPenpotConnectorService),
                _ => null
            };

            if (connectorType == null || _serviceProvider.GetService(connectorType) is not IDesignPlatformConnector connector)
            {
                throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration, $"Unsupported or unregistered design platform: {platform}");
            }

            return connector;
        }
    }

    // Common interface for all connectors
    public interface IDesignPlatformConnector
    {
        Task<TokenCollection> ExtractTokensAsync(TokensConfiguration config);
    }

    // Specific connector interfaces now inherit from the common one
    public interface IFigmaConnectorService : IDesignPlatformConnector { }
    public interface ISketchConnectorService : IDesignPlatformConnector { }
    public interface IAdobeXdConnectorService : IDesignPlatformConnector { }
    public interface IZeplinConnectorService : IDesignPlatformConnector { }
    public interface IAbstractConnectorService : IDesignPlatformConnector { }
    public interface IPenpotConnectorService : IDesignPlatformConnector { }
}