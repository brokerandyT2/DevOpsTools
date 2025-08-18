using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container.Services
{
    public interface ITagTemplateService
    {
        Task<TagTemplateResult> GenerateTagsAsync(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult,
            GitAnalysisResult gitAnalysis);

        Dictionary<string, string> GetAvailableTokens(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult,
            GitAnalysisResult gitAnalysis);
    }

    public class TagTemplateService : ITagTemplateService
    {
        private readonly ILogger<TagTemplateService> _logger;

        public TagTemplateService(ILogger<TagTemplateService> logger)
        {
            _logger = logger;
        }

        public async Task<TagTemplateResult> GenerateTagsAsync(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult,
            GitAnalysisResult gitAnalysis)
        {
            try
            {
                _logger.LogInformation("Generating tag templates");

                // Get all available token values
                var tokenValues = GetAvailableTokens(config, versionResult, gitAnalysis);

                // Generate semantic tag
                var semanticTag = await ProcessTemplateAsync(config.TagTemplate.Template, tokenValues);
                _logger.LogInformation("Semantic tag: {SemanticTag}", semanticTag);

                // Generate marketing tag  
                var marketingTag = await ProcessTemplateAsync(config.TagTemplate.MarketingTemplate, tokenValues);
                _logger.LogInformation("Marketing tag: {MarketingTag}", marketingTag);

                return new TagTemplateResult
                {
                    SemanticTag = semanticTag,
                    MarketingTag = marketingTag,
                    TokenValues = tokenValues
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tag template generation failed");
                throw new VersionDetectiveException(VersionDetectiveExitCode.TagTemplateValidationFailure,
                    $"Tag template generation failed: {ex.Message}", ex);
            }
        }

        public Dictionary<string, string> GetAvailableTokens(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult,
            GitAnalysisResult gitAnalysis)
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Basic version tokens
                tokens["version"] = versionResult.NewSemanticVersion;
                var versionParts = ParseVersion(versionResult.NewSemanticVersion);
                tokens["major"] = versionParts.major.ToString();
                tokens["minor"] = versionParts.minor.ToString();
                tokens["patch"] = versionParts.patch.ToString();

                // Repository and branch tokens
                tokens["repo"] = ExtractRepositoryName(config.RepoUrl);
                tokens["branch"] = SanitizeBranchName(config.Branch);

                // Date and time tokens
                var now = DateTime.UtcNow;
                tokens["date"] = now.ToString("yyyy-MM-dd");
                tokens["datetime"] = now.ToString("yyyy-MM-dd-HHmmss");

                // Git commit tokens
                tokens["commit-hash"] = gitAnalysis.CurrentCommit.Length > 7
                    ? gitAnalysis.CurrentCommit[..7]
                    : gitAnalysis.CurrentCommit;
                tokens["commit-hash-full"] = gitAnalysis.CurrentCommit;

                // Build and CI tokens
                tokens["build-number"] = GetBuildNumber();
                tokens["user"] = GetTriggeringUser();

                _logger.LogDebug("Generated {Count} template tokens", tokens.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate some template tokens");
            }

            return tokens;
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
                throw new VersionDetectiveException(VersionDetectiveExitCode.TagTemplateValidationFailure,
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
                   Environment.GetUserName() ??                                        // Local fallback
                   "pipeline-user";                                                    // Ultimate fallback
        }
    }
}