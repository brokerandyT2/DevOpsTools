using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;
using DesignTokenModel = x3squaredcircles.DesignToken.Generator.Models.DesignToken;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface ITokenExtractionService
    {
        Task<TokenCollection> ExtractAndProcessTokensAsync(DesignTokenConfiguration config);
        Task SaveRawTokensAsync(TokenCollection tokens, string outputDirectory);
        Task<bool> HasDesignChangesAsync(TokenCollection currentTokens, string outputDirectory);
    }

    public class TokenExtractionService : ITokenExtractionService
    {
        private readonly IDesignPlatformFactory _designPlatformFactory;
        private readonly ITokenNormalizationService _tokenNormalizationService;
        private readonly ILogger<TokenExtractionService> _logger;
        private readonly string _workingDirectory = "/src";

        public TokenExtractionService(
            IDesignPlatformFactory designPlatformFactory,
            ITokenNormalizationService tokenNormalizationService,
            ILogger<TokenExtractionService> logger)
        {
            _designPlatformFactory = designPlatformFactory;
            _tokenNormalizationService = tokenNormalizationService;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractAndProcessTokensAsync(DesignTokenConfiguration config)
        {
            try
            {
                _logger.LogInformation("Starting token extraction and processing");

                // Step 1: Extract raw tokens from design platform
                var rawTokens = await _designPlatformFactory.ExtractTokensAsync(config);
                _logger.LogInformation("✓ Extracted {TokenCount} raw tokens from {Platform}",
                    rawTokens.Tokens.Count, config.DesignPlatform.GetSelectedPlatform().ToUpperInvariant());

                // Step 2: Normalize and process tokens
                var normalizedTokens = await _tokenNormalizationService.NormalizeTokensAsync(rawTokens, config);
                _logger.LogInformation("✓ Normalized tokens - {TokenCount} tokens after processing",
                    normalizedTokens.Tokens.Count);

                // Step 3: Save raw extraction for analysis
                var outputDir = Path.Combine(_workingDirectory, config.FileManagement.OutputDir, config.FileManagement.GeneratedDir);
                await SaveRawTokensAsync(rawTokens, outputDir);

                // Step 4: Validate token collection
                ValidateTokenCollection(normalizedTokens);

                return normalizedTokens;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token extraction and processing failed");
                throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure,
                    $"Token extraction failed: {ex.Message}", ex);
            }
        }

        public async Task SaveRawTokensAsync(TokenCollection tokens, string outputDirectory)
        {
            try
            {
                _logger.LogDebug("Saving raw tokens to: {OutputDirectory}", outputDirectory);

                // Ensure output directory exists
                Directory.CreateDirectory(outputDirectory);

                // Save raw extraction data
                var rawFileName = $"{tokens.Source}-raw.json";
                var rawFilePath = Path.Combine(outputDirectory, rawFileName);

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var rawData = new
                {
                    extractionTime = DateTime.UtcNow,
                    source = tokens.Source,
                    sourceMetadata = tokens.Metadata,
                    tokenCount = tokens.Tokens.Count,
                    tokens = tokens.Tokens.Select(t => new
                    {
                        name = t.Name,
                        type = t.Type,
                        category = t.Category,
                        value = t.Value,
                        description = t.Description,
                        attributes = t.Attributes,
                        tags = t.Tags
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(rawData, jsonOptions);
                await File.WriteAllTextAsync(rawFilePath, json);

                _logger.LogInformation("✓ Saved raw token extraction: {FilePath}", rawFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save raw tokens");
                throw;
            }
        }

        public async Task<bool> HasDesignChangesAsync(TokenCollection currentTokens, string outputDirectory)
        {
            try
            {
                _logger.LogDebug("Checking for design changes since last extraction");

                var previousTokensPath = Path.Combine(outputDirectory, "processed.json");

                if (!File.Exists(previousTokensPath))
                {
                    _logger.LogInformation("No previous token extraction found - treating as new design");
                    return true;
                }

                var previousJson = await File.ReadAllTextAsync(previousTokensPath);
                var previousTokens = JsonSerializer.Deserialize<TokenCollection>(previousJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (previousTokens == null)
                {
                    _logger.LogWarning("Failed to parse previous tokens - treating as changed");
                    return true;
                }

                // Compare token collections
                var hasChanges = await CompareTokenCollectionsAsync(previousTokens, currentTokens);

                if (hasChanges)
                {
                    _logger.LogInformation("✓ Design changes detected");
                }
                else
                {
                    _logger.LogInformation("No design changes detected");
                }

                return hasChanges;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for design changes - assuming changes exist");
                return true; // Assume changes if we can't determine
            }
        }

        private async Task<bool> CompareTokenCollectionsAsync(TokenCollection previous, TokenCollection current)
        {
            try
            {
                // Quick count comparison
                if (previous.Tokens.Count != current.Tokens.Count)
                {
                    _logger.LogDebug("Token count changed: {Previous} -> {Current}",
                        previous.Tokens.Count, current.Tokens.Count);
                    return true;
                }

                // Create lookup dictionaries for efficient comparison
                var previousLookup = previous.Tokens.ToDictionary(t => t.Name, t => t);
                var currentLookup = current.Tokens.ToDictionary(t => t.Name, t => t);

                // Check for new or removed tokens
                var newTokens = currentLookup.Keys.Except(previousLookup.Keys).ToList();
                var removedTokens = previousLookup.Keys.Except(currentLookup.Keys).ToList();

                if (newTokens.Any())
                {
                    _logger.LogDebug("New tokens detected: {NewTokens}", string.Join(", ", newTokens));
                    return true;
                }

                if (removedTokens.Any())
                {
                    _logger.LogDebug("Removed tokens detected: {RemovedTokens}", string.Join(", ", removedTokens));
                    return true;
                }

                // Check for modified tokens
                foreach (var tokenName in currentLookup.Keys)
                {
                    if (previousLookup.TryGetValue(tokenName, out var previousToken))
                    {
                        var currentToken = currentLookup[tokenName];

                        if (await TokenHasChangedAsync(previousToken, currentToken))
                        {
                            _logger.LogDebug("Token modified: {TokenName}", tokenName);
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during token comparison");
                return true; // Assume changes if comparison fails
            }
        }

        private async Task<bool> TokenHasChangedAsync(DesignTokenModel previous, DesignTokenModel current)
        {
            try
            {
                // Compare basic properties
                if (previous.Type != current.Type ||
                    previous.Category != current.Category ||
                    previous.Description != current.Description)
                {
                    return true;
                }

                // Compare values (serialize to ensure consistent comparison)
                var previousValue = JsonSerializer.Serialize(previous.Value);
                var currentValue = JsonSerializer.Serialize(current.Value);

                if (previousValue != currentValue)
                {
                    return true;
                }

                // Compare attributes
                if (previous.Attributes.Count != current.Attributes.Count)
                {
                    return true;
                }

                foreach (var attr in previous.Attributes)
                {
                    if (!current.Attributes.TryGetValue(attr.Key, out var currentAttrValue) ||
                        !JsonSerializer.Serialize(attr.Value).Equals(JsonSerializer.Serialize(currentAttrValue)))
                    {
                        return true;
                    }
                }

                // Compare tags
                var previousTags = previous.Tags.OrderBy(t => t).ToList();
                var currentTags = current.Tags.OrderBy(t => t).ToList();

                if (!previousTags.SequenceEqual(currentTags))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error comparing token {TokenName} - assuming changed", previous.Name);
                return true;
            }
        }

        private void ValidateTokenCollection(TokenCollection tokens)
        {
            var errors = new List<string>();

            if (tokens.Tokens == null || !tokens.Tokens.Any())
            {
                errors.Add("No design tokens found in extraction");
            }

            // Validate token names are unique
            var duplicateNames = tokens.Tokens
                .GroupBy(t => t.Name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateNames.Any())
            {
                errors.Add($"Duplicate token names found: {string.Join(", ", duplicateNames)}");
            }

            // Validate required token properties
            var invalidTokens = tokens.Tokens
                .Where(t => string.IsNullOrWhiteSpace(t.Name) ||
                           string.IsNullOrWhiteSpace(t.Type) ||
                           t.Value == null)
                .ToList();

            if (invalidTokens.Any())
            {
                errors.Add($"Invalid tokens found - missing name, type, or value: {invalidTokens.Count} tokens");
            }

            // Validate token types are recognized
            var validTypes = new[] { "color", "typography", "spacing", "sizing", "shadow", "border", "opacity", "other" };
            var invalidTypes = tokens.Tokens
                .Where(t => !validTypes.Contains(t.Type.ToLowerInvariant()))
                .Select(t => $"{t.Name} ({t.Type})")
                .ToList();

            if (invalidTypes.Any())
            {
                _logger.LogWarning("Tokens with unrecognized types: {InvalidTypes}", string.Join(", ", invalidTypes));
            }

            if (errors.Any())
            {
                var errorMessage = "Token validation failed:\n" + string.Join("\n", errors.Select(e => $"  - {e}"));
                throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, errorMessage);
            }

            _logger.LogInformation("✓ Token collection validation passed");
        }
    }
}