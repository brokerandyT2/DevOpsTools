using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{        public interface ITokenExtractionService
        {
            Task<TokenCollection> ExtractAndProcessTokensAsync(TokensConfiguration config);
            Task<bool> HasDesignChangesAsync(TokenCollection currentTokens, TokensConfiguration config);
        }
    public class TokenExtractionService : ITokenExtractionService
    {
        private readonly IDesignPlatformFactory _designPlatformFactory;
        private readonly ITokenNormalizationService _tokenNormalizationService;
        private readonly IAppLogger _logger;
        private readonly string _workingDirectory = "/src";

        public TokenExtractionService(
            IDesignPlatformFactory designPlatformFactory,
            ITokenNormalizationService tokenNormalizationService,
            IAppLogger logger)
        {
            _designPlatformFactory = designPlatformFactory;
            _tokenNormalizationService = tokenNormalizationService;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractAndProcessTokensAsync(TokensConfiguration config)
        {
            try
            {
                _logger.LogDebug("Starting token extraction and processing workflow.");

                var rawTokens = await _designPlatformFactory.ExtractTokensAsync(config);
                _logger.LogInfo($"✓ Extracted {rawTokens.Tokens.Count} raw tokens from {config.DesignPlatform.ToUpperInvariant()}");

                var normalizedTokens = await _tokenNormalizationService.NormalizeTokensAsync(rawTokens, config);
                _logger.LogInfo($"✓ Normalized {normalizedTokens.Tokens.Count} tokens after processing");

                await SaveRawTokensForDiffing(normalizedTokens, config);

                return normalizedTokens;
            }
            catch (Exception ex)
            {
                _logger.LogError("Token extraction and processing failed.", ex);
                throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, $"Token extraction failed: {ex.Message}", ex);
            }
        }

        public async Task<bool> HasDesignChangesAsync(TokenCollection currentTokens, TokensConfiguration config)
        {
            try
            {
                _logger.LogDebug("Checking for design changes since last extraction.");
                var outputDir = GetGeneratedOutputDir(config);
                var previousTokensPath = Path.Combine(outputDir, "processed.json");

                if (!File.Exists(previousTokensPath))
                {
                    _logger.LogInfo("No previous token extraction found. Treating as a new design with changes.");
                    return true;
                }

                var previousJson = await File.ReadAllTextAsync(previousTokensPath);
                var previousTokens = JsonSerializer.Deserialize<TokenCollection>(previousJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (previousTokens == null)
                {
                    _logger.LogWarning("Failed to parse previous tokens file. Assuming changes exist.");
                    return true;
                }

                var hasChanges = !AreTokenCollectionsEqual(previousTokens, currentTokens);

                if (hasChanges) _logger.LogInfo("✓ Design changes detected.");
                else _logger.LogInfo("No design changes detected.");

                return hasChanges;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to check for design changes, assuming changes exist. Reason: {ex.Message}");
                return true;
            }
        }

        private async Task SaveRawTokensForDiffing(TokenCollection tokens, TokensConfiguration config)
        {
            var outputDir = GetGeneratedOutputDir(config);
            Directory.CreateDirectory(outputDir);
            var filePath = Path.Combine(outputDir, "processed.json");

            var json = JsonSerializer.Serialize(tokens, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            _logger.LogDebug($"Saved processed tokens for future diffing to: {filePath}");
        }

        private bool AreTokenCollectionsEqual(TokenCollection previous, TokenCollection current)
        {
            if (previous.Tokens.Count != current.Tokens.Count) return false;

            var previousLookup = previous.Tokens.ToDictionary(t => t.Name, t => JsonSerializer.Serialize(t));
            var currentLookup = current.Tokens.ToDictionary(t => t.Name, t => JsonSerializer.Serialize(t));

            if (!previousLookup.Keys.All(currentLookup.ContainsKey)) return false;

            foreach (var key in previousLookup.Keys)
            {
                if (previousLookup[key] != currentLookup[key])
                {
                    _logger.LogDebug($"Token '{key}' has changed.");
                    _logger.LogDebug($"  Previous: {previousLookup[key]}");
                    _logger.LogDebug($"  Current:  {currentLookup[key]}");
                    return false;
                }
            }
            return true;
        }

        private string GetGeneratedOutputDir(TokensConfiguration config)
        {
            // A common output directory for generated artifacts like diff files.
            return Path.Combine(_workingDirectory, "design", "generated");
        }
    }
}