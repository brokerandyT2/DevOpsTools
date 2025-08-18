using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Models;

namespace x3squaredcircles.MobileAdapter.Generator.Services
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
        private readonly GeneratorConfiguration _config;
        private readonly ILogger<TagTemplateService> _logger;

        private readonly HashSet<string> _supportedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "branch", "repo", "version", "major", "minor", "patch",
            "date", "datetime", "build-number", "user", "platform", "language", "environment", "vertical"
        };

        public TagTemplateService(
            IGitOperationsService gitOperationsService,
            GeneratorConfiguration config,
            ILogger<TagTemplateService> logger)
        {
            _gitOperationsService = gitOperationsService;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Generates a string identifier based on the configured tag template.
        /// </summary>
        /// <returns>A result object containing the resolved tag and the token values used.</returns>
        public async Task<TagTemplateResult> GenerateTagAsync()
        {
            var template = _config.TagTemplate;
            _logger.LogInformation("Generating tag from template: {Template}", template);

            try
            {
                await ValidateTemplateAsync(template);
                var tokenValues = await GetAvailableTokensAsync();
                var generatedTag = await ReplaceTokensAsync(template, tokenValues);
                var sanitizedTag = SanitizeTagForFilename(generatedTag);

                _logger.LogInformation("✓ Tag generated successfully: {GeneratedTag}", sanitizedTag);

                return new TagTemplateResult
                {
                    Template = template,
                    GeneratedTag = sanitizedTag,
                    TokenValues = tokenValues,
                    GenerationTime = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate tag from template. A default tag will be used.");
                // Ensure a valid result is always returned for forensic logging.
                return new TagTemplateResult
                {
                    Template = "default-fallback",
                    GeneratedTag = $"generation-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                    TokenValues = new Dictionary<string, string> { { "error", ex.Message } },
                    GenerationTime = DateTime.UtcNow
                };
            }
        }

        private async Task<Dictionary<string, string>> GetAvailableTokensAsync()
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Git-related tokens
            tokens["branch"] = await _gitOperationsService.GetCurrentBranchAsync();
            tokens["repo"] = await _gitOperationsService.GetRepositoryNameAsync();

            // Version tokens (simplified for this tool)
            var version = "1.0.0"; // Placeholder version
            tokens["version"] = version;
            tokens["major"] = "1";
            tokens["minor"] = "0";
            tokens["patch"] = "0";

            // Date/time tokens
            var now = DateTime.UtcNow;
            tokens["date"] = now.ToString("yyyy-MM-dd");
            tokens["datetime"] = now.ToString("yyyy-MM-dd-HHmmss");

            // Build context tokens
            tokens["build-number"] = GetBuildNumberToken();
            tokens["user"] = GetUserToken();

            // Configuration tokens
            tokens["platform"] = _config.GetSelectedPlatform().ToString().ToLowerInvariant();
            tokens["language"] = _config.GetSelectedLanguage().ToString().ToLowerInvariant();
            tokens["environment"] = _config.Environment.ToLowerInvariant();
            tokens["vertical"] = _config.Vertical?.ToLowerInvariant() ?? string.Empty;

            _logger.LogDebug("Resolved {TokenCount} template tokens.", tokens.Count);
            return tokens;
        }

        private async Task<string> ReplaceTokensAsync(string template, Dictionary<string, string> tokenValues)
        {
            var result = template;
            foreach (var token in tokenValues)
            {
                result = result.Replace($"{{{token.Key}}}", token.Value, StringComparison.OrdinalIgnoreCase);
            }
            return await Task.FromResult(result);
        }

        private Task ValidateTemplateAsync(string template)
        {
            var tokenMatches = Regex.Matches(template, @"\{([^}]+)\}");
            var unsupportedTokens = tokenMatches.Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(t => !_supportedTokens.Contains(t))
                .ToList();

            if (unsupportedTokens.Any())
            {
                var message = $"Template contains unsupported tokens: {string.Join(", ", unsupportedTokens)}. Supported tokens are: {string.Join(", ", _supportedTokens)}";
                throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, message);
            }
            return Task.CompletedTask;
        }

        private string GetBuildNumberToken()
        {
            return Environment.GetEnvironmentVariable("BUILD_BUILDID") // Azure DevOps
                ?? Environment.GetEnvironmentVariable("GITHUB_RUN_ID") // GitHub Actions
                ?? Environment.GetEnvironmentVariable("CI_PIPELINE_ID") // GitLab CI
                ?? Environment.GetEnvironmentVariable("BUILD_NUMBER") // Jenkins
                ?? DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        }

        private string GetUserToken()
        {
            return Environment.GetEnvironmentVariable("BUILD_REQUESTEDFOR") // Azure DevOps
                ?? Environment.GetEnvironmentVariable("GITHUB_ACTOR") // GitHub Actions
                ?? Environment.GetEnvironmentVariable("GITLAB_USER_LOGIN") // GitLab CI
                ?? Environment.GetEnvironmentVariable("USER") ?? Environment.GetEnvironmentVariable("USERNAME")
                ?? "system";
        }

        private string SanitizeTagForFilename(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return "unknown-tag";

            // Replace common path separators to avoid creating directories
            var sanitized = tag.Replace('/', '-').Replace('\\', '-');

            // Remove characters invalid for filenames
            var invalidChars = new string(Path.GetInvalidFileNameChars());
            var invalidRegex = new Regex($"[{Regex.Escape(invalidChars)}]");
            sanitized = invalidRegex.Replace(sanitized, "");

            // Replace multiple dashes with a single one
            sanitized = Regex.Replace(sanitized, @"-+", "-");

            return sanitized.Trim('-');
        }
    }

    /// <summary>
    /// Represents the result of the tag template resolution process.
    /// </summary>
    public class TagTemplateResult
    {
        public string Template { get; set; }
        public string GeneratedTag { get; set; }
        public Dictionary<string, string> TokenValues { get; set; } = new Dictionary<string, string>();
        public DateTime GenerationTime { get; set; }
    }
}