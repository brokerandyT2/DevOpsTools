using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{        public interface ITagTemplateService
        {
            Task<TagTemplateResult> GenerateTagAsync(TokensConfiguration config, TokenCollection tokens);
        }

    public class TagTemplateService : ITagTemplateService
    {
        private readonly IAppLogger _logger;

        public TagTemplateService(IAppLogger logger)
        {
            _logger = logger;
        }

        public Task<TagTemplateResult> GenerateTagAsync(TokensConfiguration config, TokenCollection tokens)
        {
            try
            {
                // This service is now synchronous as it no longer performs I/O.
                // It's kept as async to maintain interface consistency.
                var template = Environment.GetEnvironmentVariable("TOKENS_TAG_TEMPLATE") ?? "{branch}/{repo}/tokens/{version}";
                _logger.LogInfo($"Generating tag from template: {template}");

                var tokenValues = GetAvailableTokens(config, tokens);
                var generatedTag = ProcessTemplate(template, tokenValues);

                _logger.LogInfo($"✓ Generated tag: {generatedTag}");

                var result = new TagTemplateResult
                {
                    GeneratedTag = generatedTag,
                    TokenValues = tokenValues
                };
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("Tag template generation failed", ex);
                throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration, $"Tag template generation failed: {ex.Message}", ex);
            }
        }

        private Dictionary<string, string> GetAvailableTokens(TokensConfiguration config, TokenCollection tokens)
        {
            var tokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var versionParts = ParseVersion(tokens.Version);
            tokenValues["version"] = tokens.Version;
            tokenValues["major"] = versionParts.major.ToString();
            tokenValues["minor"] = versionParts.minor.ToString();
            tokenValues["patch"] = versionParts.patch.ToString();

            tokenValues["repo"] = ExtractRepositoryName(config.RepoUrl);
            tokenValues["branch"] = SanitizeBranchName(config.Branch);

            var now = DateTime.UtcNow;
            tokenValues["date"] = now.ToString("yyyy-MM-dd");
            tokenValues["datetime"] = now.ToString("yyyy-MM-dd-HHmmss");

            tokenValues["commit-hash"] = GetFromEnvironment("BUILD_SOURCEVERSION", "GITHUB_SHA", "CI_COMMIT_SHA").Substring(0, 7);

            tokenValues["design-platform"] = config.DesignPlatform;
            tokenValues["platform"] = config.TargetPlatform;

            return tokenValues;
        }

        private string ProcessTemplate(string template, Dictionary<string, string> tokenValues)
        {
            var result = template;
            foreach (var token in tokenValues)
            {
                result = result.Replace($"{{{token.Key}}}", token.Value, StringComparison.OrdinalIgnoreCase);
            }
            return SanitizeTagName(result);
        }

        private string SanitizeTagName(string tagName)
        {
            var sanitized = tagName;
            var problemChars = new[] { ' ', '\t', '\n', '\r', '~', '^', ':', '?', '*', '[', '\\' };
            foreach (var problemChar in problemChars)
            {
                sanitized = sanitized.Replace(problemChar, '-');
            }
            return sanitized.Trim('.', '-');
        }

        private (int major, int minor, int patch) ParseVersion(string version)
        {
            try
            {
                var parts = version.Split('.');
                return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
            }
            catch { return (1, 0, 0); }
        }

        private string ExtractRepositoryName(string repoUrl)
        {
            if (string.IsNullOrEmpty(repoUrl)) return "unknown-repo";
            return SanitizeNameToken(repoUrl.Split('/').LastOrDefault()?.Replace(".git", "") ?? "unknown-repo");
        }

        private string SanitizeBranchName(string branchName)
        {
            if (string.IsNullOrEmpty(branchName)) return "unknown-branch";
            return SanitizeNameToken(branchName.Replace("refs/heads/", ""));
        }

        private string SanitizeNameToken(string name)
        {
            return new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray())
                .Replace("--", "-").Trim('-');
        }

        private string GetFromEnvironment(params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return "unknown";
        }
    }
}