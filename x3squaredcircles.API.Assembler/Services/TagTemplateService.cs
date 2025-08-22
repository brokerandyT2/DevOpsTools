using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for a service that resolves a template string into a build tag/identifier.
    /// </summary>
    public interface ITagTemplateService
    {
        Task<TagTemplateResult> GenerateTagAsync();
    }

    /// <summary>
    /// Service responsible for resolving a template string by replacing tokens with contextual information
    /// from Git, the build environment, and the application configuration.
    /// </summary>
    public class TagTemplateService : ITagTemplateService
    {
        private readonly IGitOperationsService _gitOperationsService;
        private readonly AssemblerConfiguration _config;
        private readonly ILogger<TagTemplateService> _logger;

        // The single source of truth for all supported tokens in this service.
        private readonly HashSet<string> _supportedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "branch", "repo", "version", "major", "minor", "patch",
            "date", "datetime", "commit-hash", "cloud", "group"
        };

        public TagTemplateService(
            IGitOperationsService gitOperationsService,
            AssemblerConfiguration config,
            ILogger<TagTemplateService> logger)
        {
            _gitOperationsService = gitOperationsService;
            _config = config;
            _logger = logger;
        }

        public async Task<TagTemplateResult> GenerateTagAsync()
        {
            var template = _config.TagTemplate.Template;
            _logger.LogInformation("Generating tag from template: {Template}", template);

            try
            {
                ValidateTemplate(template);
                var tokenValues = await GetAvailableTokensAsync();
                var generatedTag = ReplaceTokens(template, tokenValues);

                _logger.LogInformation("✓ Tag generated successfully: {GeneratedTag}", generatedTag);

                return new TagTemplateResult
                {
                    Template = template,
                    GeneratedTag = generatedTag,
                    TokenValues = tokenValues,
                    GenerationTime = DateTime.UtcNow
                };
            }
            catch (AssemblerException)
            {
                // Re-throw our specific, handled exceptions to fail the pipeline cleanly.
                throw;
            }
            catch (Exception ex)
            {
                var fallbackTag = $"generation-error-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                _logger.LogError(ex, "Failed to generate tag from template. Using fallback tag: {Tag}", fallbackTag);
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Tag generation failed: {ex.Message}", ex);
            }
        }

        private async Task<Dictionary<string, string>> GetAvailableTokensAsync()
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            tokens["branch"] = await _gitOperationsService.GetCurrentBranchAsync();
            tokens["repo"] = await _gitOperationsService.GetRepositoryNameAsync();
            tokens["commit-hash"] = await _gitOperationsService.GetCommitHashAsync();

            // Placeholder for a more sophisticated versioning scheme from Keystone.
            // For Assembler, a static version is acceptable as a fallback.
            var version = "1.0.0";
            tokens["version"] = version;
            var versionParts = version.Split('.');
            tokens["major"] = versionParts.Length > 0 ? versionParts[0] : "1";
            tokens["minor"] = versionParts.Length > 1 ? versionParts[1] : "0";
            tokens["patch"] = versionParts.Length > 2 ? versionParts[2] : "0";

            var now = DateTime.UtcNow;
            tokens["date"] = now.ToString("yyyy-MM-dd");
            tokens["datetime"] = now.ToString("yyyy-MM-dd-HHmmss");

            tokens["cloud"] = _config.Cloud;

            // Note: {group} is a contextual token. It will be replaced with a placeholder here
            // and can be replaced with the actual group name by the consumer of this result.
            tokens["group"] = "{group}";

            _logger.LogDebug("Resolved {TokenCount} template tokens.", tokens.Count);
            return tokens;
        }

        private string ReplaceTokens(string template, Dictionary<string, string> tokenValues)
        {
            var result = template;
            foreach (var token in tokenValues)
            {
                result = result.Replace($"{{{token.Key}}}", token.Value, StringComparison.OrdinalIgnoreCase);
            }
            return SanitizeTag(result);
        }

        private string SanitizeTag(string tag)
        {
            // Replace invalid characters for Git tags or Docker tags with a hyphen.
            // This is a basic sanitization; more complex rules could be added.
            var sanitized = Regex.Replace(tag, @"[^a-zA-Z0-9_.-]", "-");
            return sanitized;
        }

        private void ValidateTemplate(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "Tag template string cannot be empty.");
            }

            var tokenMatches = Regex.Matches(template, @"\{([^}]+)\}");
            var unsupportedTokens = tokenMatches.Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(t => !_supportedTokens.Contains(t))
                .ToList();

            if (unsupportedTokens.Any())
            {
                var message = $"Template '{template}' contains unsupported tokens: {string.Join(", ", unsupportedTokens.Select(t => $"{{{t}}}"))}. Supported tokens are: {string.Join(", ", _supportedTokens)}";
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, message);
            }
        }
    }
}