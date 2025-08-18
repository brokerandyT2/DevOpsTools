using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
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
            var template = _config.TagTemplate;
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
            catch (Exception ex)
            {
                var fallbackTag = $"generation-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                _logger.LogError(ex, "Failed to generate tag from template. Using fallback tag: {Tag}", fallbackTag);
                return new TagTemplateResult
                {
                    Template = "fallback",
                    GeneratedTag = fallbackTag,
                    TokenValues = new Dictionary<string, string> { { "error", ex.Message } },
                    GenerationTime = DateTime.UtcNow
                };
            }
        }

        private async Task<Dictionary<string, string>> GetAvailableTokensAsync()
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            tokens["branch"] = await _gitOperationsService.GetCurrentBranchAsync();
            tokens["repo"] = await _gitOperationsService.GetRepositoryNameAsync();
            tokens["commit-hash"] = await _gitOperationsService.GetCommitHashAsync();

            var version = "1.0.0"; // Placeholder for a more sophisticated versioning scheme
            tokens["version"] = version;
            var versionParts = version.Split('.');
            tokens["major"] = versionParts.Length > 0 ? versionParts[0] : "1";
            tokens["minor"] = versionParts.Length > 1 ? versionParts[1] : "0";
            tokens["patch"] = versionParts.Length > 2 ? versionParts[2] : "0";

            var now = DateTime.UtcNow;
            tokens["date"] = now.ToString("yyyy-MM-dd");
            tokens["datetime"] = now.ToString("yyyy-MM-dd-HHmmss");

            tokens["cloud"] = _config.Cloud;

            // Note: {group} is contextual and will be replaced per-group if needed by downstream consumers.
            // For a single run-level tag, it's often omitted or replaced with a placeholder.
            tokens["group"] = "multi-group";

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
            return result;
        }

        private void ValidateTemplate(string template)
        {
            var tokenMatches = Regex.Matches(template, @"\{([^}]+)\}");
            var unsupportedTokens = tokenMatches.Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(t => !_supportedTokens.Contains(t))
                .ToList();

            if (unsupportedTokens.Any())
            {
                var message = $"Template contains unsupported tokens: {string.Join(", ", unsupportedTokens)}. Supported tokens are: {string.Join(", ", _supportedTokens)}";
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, message);
            }
        }
    }

    /// <summary>
    /// Represents the result of the tag template resolution process.
    /// </summary>
    public class TagTemplateResult
    {
        public string Template { get; set; } = string.Empty;
        public string GeneratedTag { get; set; } = string.Empty;
        public Dictionary<string, string> TokenValues { get; set; } = new Dictionary<string, string>();
        public DateTime GenerationTime { get; set; }
    }
}