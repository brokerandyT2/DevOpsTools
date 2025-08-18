using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface ITagTemplateService
    {
        Task<TagTemplateResult> GenerateTagAsync(SqlSchemaConfiguration config, EntityDiscoveryResult entities);
        Task<bool> ValidateTemplateAsync(string template);
        Task<Dictionary<string, string>> GetAvailableTokensAsync(SqlSchemaConfiguration config, EntityDiscoveryResult entities);
    }

    public class TagTemplateService : ITagTemplateService
    {
        private readonly IGitOperationsService _gitOperationsService;
        private readonly ILogger<TagTemplateService> _logger;

        private readonly HashSet<string> _supportedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "branch", "repo", "version", "major", "minor", "patch",
            "date", "datetime", "commit-hash", "commit-hash-full",
            "build-number", "user", "database", "environment", "vertical"
        };

        public TagTemplateService(IGitOperationsService gitOperationsService, ILogger<TagTemplateService> logger)
        {
            _gitOperationsService = gitOperationsService;
            _logger = logger;
        }

        public async Task<TagTemplateResult> GenerateTagAsync(SqlSchemaConfiguration config, EntityDiscoveryResult entities)
        {
            try
            {
                _logger.LogInformation("Generating tag from template: {Template}", config.TagTemplate.Template);

                var template = config.TagTemplate.Template;
                if (string.IsNullOrWhiteSpace(template))
                {
                    template = "{branch}/{repo}/schema/{version}";
                    _logger.LogDebug("Using default tag template: {Template}", template);
                }

                // Validate template format
                await ValidateTemplateAsync(template);

                // Get all token values
                var tokenValues = await GetAvailableTokensAsync(config, entities);

                // Replace tokens in template
                var generatedTag = await ReplaceTokensAsync(template, tokenValues);

                // Sanitize the tag for git compatibility
                var sanitizedTag = SanitizeGitTag(generatedTag);

                var result = new TagTemplateResult
                {
                    Template = template,
                    GeneratedTag = sanitizedTag,
                    TokenValues = tokenValues,
                    GenerationTime = DateTime.UtcNow,
                    Metadata = new Dictionary<string, object>
                    {
                        ["original_tag"] = generatedTag,
                        ["sanitized"] = !generatedTag.Equals(sanitizedTag, StringComparison.Ordinal),
                        ["entity_count"] = entities.Entities.Count,
                        ["tokens_resolved"] = tokenValues.Count
                    }
                };

                _logger.LogInformation("✓ Tag generated successfully: {GeneratedTag}", sanitizedTag);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate tag from template");
                throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration,
                    $"Failed to generate tag from template: {ex.Message}", ex);
            }
        }

        public async Task<bool> ValidateTemplateAsync(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new ArgumentException("Tag template cannot be empty", nameof(template));
            }

            // Find all tokens in the template
            var tokenMatches = Regex.Matches(template, @"\{([^}]+)\}", RegexOptions.IgnoreCase);
            var unsupportedTokens = new List<string>();

            foreach (Match match in tokenMatches)
            {
                var token = match.Groups[1].Value;
                if (!_supportedTokens.Contains(token))
                {
                    unsupportedTokens.Add(token);
                }
            }

            if (unsupportedTokens.Any())
            {
                var supportedList = string.Join(", ", _supportedTokens.Select(t => $"{{{t}}}"));
                throw new ArgumentException(
                    $"Unsupported tokens found: {string.Join(", ", unsupportedTokens.Select(t => $"{{{t}}}"))}. " +
                    $"Supported tokens: {supportedList}");
            }

            // Check for unclosed braces
            var openBraces = template.Count(c => c == '{');
            var closeBraces = template.Count(c => c == '}');
            if (openBraces != closeBraces)
            {
                throw new ArgumentException("Mismatched braces in tag template");
            }

            // Check for nested braces
            if (Regex.IsMatch(template, @"\{[^}]*\{"))
            {
                throw new ArgumentException("Nested braces are not supported in tag templates");
            }

            return true;
        }

        public async Task<Dictionary<string, string>> GetAvailableTokensAsync(SqlSchemaConfiguration config, EntityDiscoveryResult entities)
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Git-related tokens
                tokens["branch"] = await GetBranchTokenAsync();
                tokens["repo"] = await GetRepositoryTokenAsync();
                tokens["commit-hash"] = await GetCommitHashTokenAsync();
                tokens["commit-hash-full"] = await GetCommitHashFullTokenAsync();

                // Version tokens
                var version = GenerateSchemaVersion(entities);
                tokens["version"] = version;
                var versionParts = ParseVersion(version);
                tokens["major"] = versionParts.Major.ToString();
                tokens["minor"] = versionParts.Minor.ToString();
                tokens["patch"] = versionParts.Patch.ToString();

                // Date/time tokens
                var now = DateTime.UtcNow;
                tokens["date"] = now.ToString("yyyy-MM-dd");
                tokens["datetime"] = now.ToString("yyyy-MM-dd-HHmmss");

                // Build context tokens
                tokens["build-number"] = GetBuildNumberToken();
                tokens["user"] = GetUserToken();

                // Configuration tokens
                tokens["database"] = config.Database.GetSelectedProvider();
                tokens["environment"] = config.Environment.Environment.ToLowerInvariant();
                tokens["vertical"] = config.Environment.Vertical?.ToLowerInvariant() ?? "";

                _logger.LogDebug("Generated {TokenCount} template tokens", tokens.Count);
                return tokens;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error generating some template tokens, using fallback values");

                // Ensure we have fallback values for all tokens
                EnsureFallbackTokens(tokens, config, entities);
                return tokens;
            }
        }

        private async Task<string> ReplaceTokensAsync(string template, Dictionary<string, string> tokenValues)
        {
            var result = template;

            foreach (var token in tokenValues)
            {
                var placeholder = $"{{{token.Key}}}";
                result = result.Replace(placeholder, token.Value, StringComparison.OrdinalIgnoreCase);
            }

            // Check for any unreplaced tokens
            var remainingTokens = Regex.Matches(result, @"\{([^}]+)\}");
            if (remainingTokens.Count > 0)
            {
                var unreplacedTokens = remainingTokens.Cast<Match>().Select(m => m.Groups[1].Value).Distinct();
                _logger.LogWarning("Unreplaced tokens in template: {UnreplacedTokens}", string.Join(", ", unreplacedTokens));
            }

            return result;
        }

        private async Task<string> GetBranchTokenAsync()
        {
            try
            {
                var branch = await _gitOperationsService.GetCurrentBranchAsync();
                return SanitizeForTag(branch);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get current branch, using fallback");
                return "unknown";
            }
        }

        private async Task<string> GetRepositoryTokenAsync()
        {
            try
            {
                var repo = await _gitOperationsService.GetRepositoryNameAsync();
                return SanitizeForTag(repo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get repository name, using fallback");
                return "unknown-repo";
            }
        }

        private async Task<string> GetCommitHashTokenAsync()
        {
            try
            {
                return await _gitOperationsService.GetCurrentCommitHashAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get commit hash, using fallback");
                return "unknown";
            }
        }

        private async Task<string> GetCommitHashFullTokenAsync()
        {
            try
            {
                return await _gitOperationsService.GetCurrentCommitHashFullAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get full commit hash, using fallback");
                return "unknown";
            }
        }

        private string GenerateSchemaVersion(EntityDiscoveryResult entities)
        {
            try
            {
                // Generate a semantic version based on entity analysis
                var major = 1;
                var minor = CalculateMinorVersion(entities);
                var patch = CalculatePatchVersion(entities);

                return $"{major}.{minor}.{patch}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate schema version, using default");
                return "1.0.0";
            }
        }

        private int CalculateMinorVersion(EntityDiscoveryResult entities)
        {
            // Base minor version on number of entities
            // This is a simple heuristic - could be made more sophisticated
            var entityCount = entities.Entities.Count;

            if (entityCount == 0) return 0;
            if (entityCount <= 5) return 1;
            if (entityCount <= 15) return 2;
            if (entityCount <= 30) return 3;

            return Math.Min(entityCount / 10, 9);
        }

        private int CalculatePatchVersion(EntityDiscoveryResult entities)
        {
            // Base patch version on total properties and relationships
            var totalProperties = entities.Entities.Sum(e => e.Properties.Count);
            var totalRelationships = entities.Entities.Sum(e => e.Relationships.Count);
            var complexity = totalProperties + totalRelationships;

            return Math.Min(complexity % 100, 99);
        }

        private (int Major, int Minor, int Patch) ParseVersion(string version)
        {
            try
            {
                var parts = version.Split('.');
                return (
                    Major: parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : 1,
                    Minor: parts.Length > 1 && int.TryParse(parts[1], out var minor) ? minor : 0,
                    Patch: parts.Length > 2 && int.TryParse(parts[2], out var patch) ? patch : 0
                );
            }
            catch
            {
                return (1, 0, 0);
            }
        }

        private string GetBuildNumberToken()
        {
            // Try various CI/CD build number environment variables
            var buildNumber = Environment.GetEnvironmentVariable("BUILD_BUILDID") ??
                             Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ??
                             Environment.GetEnvironmentVariable("CI_PIPELINE_ID") ??
                             Environment.GetEnvironmentVariable("BUILD_NUMBER") ??
                             Environment.GetEnvironmentVariable("BUILDKITE_BUILD_NUMBER") ??
                             Environment.GetEnvironmentVariable("TRAVIS_BUILD_NUMBER") ??
                             Environment.GetEnvironmentVariable("CIRCLE_BUILD_NUM");

            if (!string.IsNullOrEmpty(buildNumber))
            {
                return SanitizeForTag(buildNumber);
            }

            // Fallback to timestamp-based build number
            return DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        }

        private string GetUserToken()
        {
            // Try various user environment variables from different CI/CD systems
            var user = Environment.GetEnvironmentVariable("BUILD_REQUESTEDFOR") ??          // Azure DevOps
                      Environment.GetEnvironmentVariable("GITHUB_ACTOR") ??                // GitHub Actions
                      Environment.GetEnvironmentVariable("GITLAB_USER_LOGIN") ??          // GitLab CI
                      Environment.GetEnvironmentVariable("USER") ??                        // Unix
                      Environment.GetEnvironmentVariable("USERNAME") ??                    // Windows
                      Environment.GetEnvironmentVariable("LOGNAME");                       // Unix fallback

            if (!string.IsNullOrEmpty(user))
            {
                return SanitizeForTag(user);
            }

            return "system";
        }

        private void EnsureFallbackTokens(Dictionary<string, string> tokens, SqlSchemaConfiguration config, EntityDiscoveryResult entities)
        {
            var fallbacks = new Dictionary<string, string>
            {
                ["branch"] = "main",
                ["repo"] = "unknown-repo",
                ["version"] = "1.0.0",
                ["major"] = "1",
                ["minor"] = "0",
                ["patch"] = "0",
                ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                ["datetime"] = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss"),
                ["commit-hash"] = "unknown",
                ["commit-hash-full"] = "unknown",
                ["build-number"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                ["user"] = "system",
                ["database"] = config.Database.GetSelectedProvider(),
                ["environment"] = config.Environment.Environment.ToLowerInvariant(),
                ["vertical"] = config.Environment.Vertical?.ToLowerInvariant() ?? ""
            };

            foreach (var fallback in fallbacks)
            {
                if (!tokens.ContainsKey(fallback.Key) || string.IsNullOrEmpty(tokens[fallback.Key]))
                {
                    tokens[fallback.Key] = fallback.Value;
                }
            }
        }

        private string SanitizeForTag(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "unknown";

            // Replace invalid characters with hyphens
            var sanitized = Regex.Replace(input, @"[^a-zA-Z0-9\-_./]", "-");

            // Remove multiple consecutive hyphens/underscores
            sanitized = Regex.Replace(sanitized, @"[-_]{2,}", "-");

            // Remove leading/trailing hyphens
            sanitized = sanitized.Trim('-', '_');

            // Ensure it's not empty after sanitization
            if (string.IsNullOrEmpty(sanitized))
                return "unknown";

            return sanitized;
        }

        private string SanitizeGitTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return "schema-unknown";

            // Git tag rules:
            // - Cannot start or end with a dot
            // - Cannot contain ".." 
            // - Cannot contain spaces
            // - Cannot contain certain special characters

            var sanitized = tag;

            // Replace spaces with hyphens
            sanitized = sanitized.Replace(' ', '-');

            // Remove invalid characters for git tags
            sanitized = Regex.Replace(sanitized, @"[~^:\?*\[\]\\]", "");

            // Handle consecutive dots
            sanitized = Regex.Replace(sanitized, @"\.{2,}", ".");

            // Remove leading/trailing dots
            sanitized = sanitized.Trim('.');

            // Ensure it doesn't start with a hyphen (some git hosting services don't like this)
            sanitized = sanitized.TrimStart('-');

            // Limit length to reasonable size
            if (sanitized.Length > 250)
            {
                sanitized = sanitized.Substring(0, 250).TrimEnd('-', '.');
            }

            // Ensure we have a valid tag
            if (string.IsNullOrEmpty(sanitized) || sanitized.Length < 1)
            {
                sanitized = $"schema-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            }

            return sanitized;
        }
    }

    public class TagTemplateResult
    {
        public string Template { get; set; } = string.Empty;
        public string GeneratedTag { get; set; } = string.Empty;
        public Dictionary<string, string> TokenValues { get; set; } = new Dictionary<string, string>();
        public DateTime GenerationTime { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}