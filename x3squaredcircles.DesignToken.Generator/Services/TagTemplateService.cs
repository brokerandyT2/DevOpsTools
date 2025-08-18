using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface ITagTemplateService
    {
        Task<TagTemplateResult> GenerateTagAsync(
            DesignTokenConfiguration config,
            TokenCollection tokens);

        Dictionary<string, string> GetAvailableTokens(
            DesignTokenConfiguration config,
            TokenCollection tokens);
    }

    public class TagTemplateService : ITagTemplateService
    {
        private readonly ILogger<TagTemplateService> _logger;

        public TagTemplateService(ILogger<TagTemplateService> logger)
        {
            _logger = logger;
        }

        public async Task<TagTemplateResult> GenerateTagAsync(
            DesignTokenConfiguration config,
            TokenCollection tokens)
        {
            try
            {
                _logger.LogInformation("Generating tag from template: {Template}", config.TagTemplate.Template);

                // Get all available token values
                var tokenValues = GetAvailableTokens(config, tokens);

                // Generate tag from template
                var generatedTag = await ProcessTemplateAsync(config.TagTemplate.Template, tokenValues);
                _logger.LogInformation("Generated tag: {GeneratedTag}", generatedTag);

                return new TagTemplateResult
                {
                    GeneratedTag = generatedTag,
                    TokenValues = tokenValues
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tag template generation failed");
                throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration,
                    $"Tag template generation failed: {ex.Message}", ex);
            }
        }

        public Dictionary<string, string> GetAvailableTokens(
            DesignTokenConfiguration config,
            TokenCollection tokens)
        {
            var tokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Basic version tokens
                tokenValues["version"] = tokens.Version;
                var versionParts = ParseVersion(tokens.Version);
                tokenValues["major"] = versionParts.major.ToString();
                tokenValues["minor"] = versionParts.minor.ToString();
                tokenValues["patch"] = versionParts.patch.ToString();

                // Repository and branch tokens
                tokenValues["repo"] = ExtractRepositoryName(config.RepoUrl);
                tokenValues["branch"] = SanitizeBranchName(config.Branch);

                // Date and time tokens
                var now = DateTime.UtcNow;
                tokenValues["date"] = now.ToString("yyyy-MM-dd");
                tokenValues["datetime"] = now.ToString("yyyy-MM-dd-HHmmss");

                // Git commit tokens (would need git integration)
                tokenValues["commit-hash"] = GetCommitHash();
                tokenValues["commit-hash-full"] = GetFullCommitHash();

                // Build and CI tokens
                tokenValues["build-number"] = GetBuildNumber();
                tokenValues["user"] = GetTriggeringUser();

                // Design platform and target platform tokens
                tokenValues["design-platform"] = config.DesignPlatform.GetSelectedPlatform();
                tokenValues["platform"] = config.TargetPlatform.GetSelectedPlatform();

                // Vertical/business unit token
                tokenValues["vertical"] = ExtractVerticalName(config);

                _logger.LogDebug("Generated {Count} template tokens", tokenValues.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate some template tokens");
            }

            return tokenValues;
        }

        private async Task<string> ProcessTemplateAsync(string template, Dictionary<string, string> tokenValues)
        {
            if (string.IsNullOrEmpty(template))
            {
                throw new ArgumentException("Template cannot be empty");
            }

            var result = template;

            // Replace all tokens in the template
            foreach (var token in tokenValues)
            {
                var tokenPattern = $"{{{token.Key}}}";
                result = result.Replace(tokenPattern, token.Value, StringComparison.OrdinalIgnoreCase);
            }

            // Check for any unreplaced tokens
            var unreplacedTokens = FindUnreplacedTokens(result);
            if (unreplacedTokens.Count > 0)
            {
                _logger.LogWarning("Template contains unreplaced tokens: {Tokens}", string.Join(", ", unreplacedTokens));
                throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration,
                    $"Template contains unknown tokens: {string.Join(", ", unreplacedTokens)}");
            }

            // Sanitize the final tag name
            result = SanitizeTagName(result);

            return result;
        }

        private List<string> FindUnreplacedTokens(string processedTemplate)
        {
            var tokens = new List<string>();
            var startIndex = 0;

            while (true)
            {
                var openBrace = processedTemplate.IndexOf('{', startIndex);
                if (openBrace == -1) break;

                var closeBrace = processedTemplate.IndexOf('}', openBrace);
                if (closeBrace == -1) break;

                var token = processedTemplate.Substring(openBrace, closeBrace - openBrace + 1);
                tokens.Add(token);
                startIndex = closeBrace + 1;
            }

            return tokens;
        }

        private string ExtractRepositoryName(string repoUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(repoUrl))
                    return "unknown-repo";

                // Handle different URL formats
                var uri = new Uri(repoUrl);
                var path = uri.AbsolutePath.Trim('/');

                // Remove .git suffix if present
                if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                {
                    path = path[..^4];
                }

                // Get the last part of the path (repository name)
                var parts = path.Split('/');
                var repoName = parts.Length > 0 ? parts[^1] : "unknown-repo";

                return SanitizeNameToken(repoName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract repository name from URL: {RepoUrl}", repoUrl);
                return "unknown-repo";
            }
        }

        private string SanitizeBranchName(string branchName)
        {
            if (string.IsNullOrEmpty(branchName))
                return "unknown-branch";

            // Remove common prefixes
            var sanitized = branchName;
            if (sanitized.StartsWith("refs/heads/"))
                sanitized = sanitized[11..];
            if (sanitized.StartsWith("origin/"))
                sanitized = sanitized[7..];

            return SanitizeNameToken(sanitized);
        }

        private string SanitizeNameToken(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "unknown";

            // Replace invalid characters with hyphens
            var sanitized = name;
            var invalidChars = new[] { ' ', '/', '\\', ':', '*', '?', '"', '<', '>', '|', '@', '#', '$', '%', '^', '&' };

            foreach (var invalidChar in invalidChars)
            {
                sanitized = sanitized.Replace(invalidChar, '-');
            }

            // Remove consecutive hyphens
            while (sanitized.Contains("--"))
            {
                sanitized = sanitized.Replace("--", "-");
            }

            // Trim hyphens from start and end
            sanitized = sanitized.Trim('-');

            // Ensure it's not empty
            if (string.IsNullOrEmpty(sanitized))
                sanitized = "unknown";

            return sanitized.ToLowerInvariant();
        }

        private string SanitizeTagName(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
                return "unknown-tag";

            // Git tag names have specific restrictions
            var sanitized = tagName;

            // Replace problematic characters
            var problemChars = new[] { ' ', '\t', '\n', '\r', '~', '^', ':', '?', '*', '[', '\\' };
            foreach (var problemChar in problemChars)
            {
                sanitized = sanitized.Replace(problemChar, '-');
            }

            // Remove consecutive dots
            while (sanitized.Contains(".."))
            {
                sanitized = sanitized.Replace("..", ".");
            }

            // Remove consecutive hyphens
            while (sanitized.Contains("--"))
            {
                sanitized = sanitized.Replace("--", "-");
            }

            // Can't start or end with dot or hyphen
            sanitized = sanitized.Trim('.', '-');

            // Can't end with .lock
            if (sanitized.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                sanitized = sanitized[..^5] + "-lock";
            }

            // Ensure it's not empty
            if (string.IsNullOrEmpty(sanitized))
                sanitized = "unknown-tag";

            return sanitized;
        }

        private (int major, int minor, int patch) ParseVersion(string version)
        {
            try
            {
                var parts = version.Split('.');
                var major = parts.Length > 0 ? int.Parse(parts[0]) : 1;
                var minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                var patch = parts.Length > 2 ? int.Parse(parts[2]) : 0;

                return (major, minor, patch);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse version: {Version}", version);
                return (1, 0, 0);
            }
        }

        private string GetCommitHash()
        {
            // Try to get short commit hash from various CI/CD environments
            var fullHash = GetFullCommitHash();
            return fullHash.Length > 7 ? fullHash[..7] : fullHash;
        }

        private string GetFullCommitHash()
        {
            // Try to get commit hash from various CI/CD environments
            return Environment.GetEnvironmentVariable("BUILD_SOURCEVERSION") ??       // Azure DevOps
                   Environment.GetEnvironmentVariable("GITHUB_SHA") ??                // GitHub Actions
                   Environment.GetEnvironmentVariable("CI_COMMIT_SHA") ??             // GitLab CI
                   Environment.GetEnvironmentVariable("GIT_COMMIT") ??                // Jenkins
                   Environment.GetEnvironmentVariable("BUILDKITE_COMMIT") ??          // Buildkite
                   Environment.GetEnvironmentVariable("CIRCLE_SHA1") ??               // CircleCI
                   Environment.GetEnvironmentVariable("TRAVIS_COMMIT") ??             // Travis CI
                   DateTime.UtcNow.ToString("yyyyMMddHHmmss");                       // Fallback
        }

        private string GetBuildNumber()
        {
            // Try to get build number from various CI/CD environments
            return Environment.GetEnvironmentVariable("BUILD_BUILDID") ??           // Azure DevOps
                   Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER") ??       // GitHub Actions  
                   Environment.GetEnvironmentVariable("CI_PIPELINE_ID") ??          // GitLab CI
                   Environment.GetEnvironmentVariable("BUILD_NUMBER") ??            // Jenkins
                   Environment.GetEnvironmentVariable("BUILDKITE_BUILD_NUMBER") ??  // Buildkite
                   Environment.GetEnvironmentVariable("CIRCLE_BUILD_NUM") ??        // CircleCI
                   Environment.GetEnvironmentVariable("TRAVIS_BUILD_NUMBER") ??     // Travis CI
                   DateTime.UtcNow.ToString("yyyyMMddHHmmss");                     // Fallback
        }

        private string GetTriggeringUser()
        {
            // Try to get user from various CI/CD environments
            return Environment.GetEnvironmentVariable("BUILD_REQUESTEDFOR") ??         // Azure DevOps
                   Environment.GetEnvironmentVariable("GITHUB_ACTOR") ??               // GitHub Actions
                   Environment.GetEnvironmentVariable("GITLAB_USER_LOGIN") ??          // GitLab CI
                   Environment.GetEnvironmentVariable("BUILD_USER") ??                 // Jenkins
                   Environment.GetEnvironmentVariable("BUILDKITE_BUILD_CREATOR") ??    // Buildkite
                   Environment.GetEnvironmentVariable("CIRCLE_USERNAME") ??            // CircleCI
                   Environment.GetEnvironmentVariable("TRAVIS_BUILD_USER") ??          // Travis CI
                   Environment.UserName ??                                             // Local fallback
                   "pipeline-user";                                                    // Ultimate fallback
        }

        private string ExtractVerticalName(DesignTokenConfiguration config)
        {
            try
            {
                // Try to extract vertical from repository URL
                var repoUrl = config.RepoUrl;
                if (!string.IsNullOrEmpty(repoUrl))
                {
                    var uri = new Uri(repoUrl);
                    var pathParts = uri.AbsolutePath.Trim('/').Split('/');

                    // Look for common vertical indicators in repo name
                    var repoName = pathParts.LastOrDefault()?.Replace(".git", "");
                    if (!string.IsNullOrEmpty(repoName))
                    {
                        var verticals = new[] { "photography", "location", "navigation", "commerce", "health", "finance" };
                        foreach (var vertical in verticals)
                        {
                            if (repoName.Contains(vertical, StringComparison.OrdinalIgnoreCase))
                            {
                                return vertical.ToLowerInvariant();
                            }
                        }
                    }
                }

                // Try to extract from target repo
                if (!string.IsNullOrEmpty(config.TargetRepo))
                {
                    var verticals = new[] { "photography", "location", "navigation", "commerce", "health", "finance" };
                    foreach (var vertical in verticals)
                    {
                        if (config.TargetRepo.Contains(vertical, StringComparison.OrdinalIgnoreCase))
                        {
                            return vertical.ToLowerInvariant();
                        }
                    }
                }

                return "tokens";
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract vertical name");
                return "tokens";
            }
        }
    }
}