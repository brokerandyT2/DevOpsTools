using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container.Services
{
    public interface IVersionCalculationService
    {
        Task<VersionCalculationResult> CalculateVersionAsync(
            VersionDetectiveConfiguration config,
            LanguageAnalysisResult languageAnalysis,
            GitAnalysisResult gitAnalysis);
    }

    public class VersionCalculationService : IVersionCalculationService
    {
        private readonly ILogger<VersionCalculationService> _logger;

        public VersionCalculationService(ILogger<VersionCalculationService> logger)
        {
            _logger = logger;
        }

        public async Task<VersionCalculationResult> CalculateVersionAsync(
            VersionDetectiveConfiguration config,
            LanguageAnalysisResult languageAnalysis,
            GitAnalysisResult gitAnalysis)
        {
            try
            {
                _logger.LogInformation("Calculating version impact based on analysis results");

                // Determine current version
                var currentVersion = await DetermineCurrentVersionAsync(config, gitAnalysis);
                _logger.LogInformation("Current version: {CurrentVersion}", currentVersion);

                // Calculate version increments
                var versionIncrement = CalculateVersionIncrement(languageAnalysis);
                _logger.LogInformation("Version increment: Major={Major}, Minor={Minor}, Patch={Patch}",
                    versionIncrement.Major, versionIncrement.Minor, versionIncrement.Patch);

                // Apply increments to get new versions
                var newSemanticVersion = ApplyVersionIncrement(currentVersion, versionIncrement);
                var newMarketingVersion = CalculateMarketingVersion(currentVersion, languageAnalysis);

                // Generate reasoning
                var reasoning = GenerateReasoning(languageAnalysis, versionIncrement);

                var result = new VersionCalculationResult
                {
                    CurrentVersion = currentVersion,
                    NewSemanticVersion = newSemanticVersion,
                    NewMarketingVersion = newMarketingVersion,
                    HasMajorChanges = versionIncrement.Major > 0,
                    MinorChanges = versionIncrement.Minor,
                    PatchChanges = versionIncrement.Patch,
                    Reasoning = reasoning,
                    MajorChanges = languageAnalysis.EntityChanges.Where(c => c.IsMajorChange).ToList()
                };

                _logger.LogInformation("Version calculation complete: {Current} → {Semantic} (Marketing: {Marketing})",
                    currentVersion, newSemanticVersion, newMarketingVersion);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Version calculation failed");
                throw new VersionDetectiveException(VersionDetectiveExitCode.InvalidConfiguration,
                    $"Version calculation failed: {ex.Message}", ex);
            }
        }

        private async Task<string> DetermineCurrentVersionAsync(VersionDetectiveConfiguration config, GitAnalysisResult gitAnalysis)
        {
            // If we have a baseline commit, try to extract version from it
            if (!string.IsNullOrEmpty(gitAnalysis.BaselineCommit))
            {
                var baselineVersion = ExtractVersionFromCommit(gitAnalysis.BaselineCommit);
                if (!string.IsNullOrEmpty(baselineVersion))
                {
                    return baselineVersion;
                }
            }

            // Try to extract from FROM parameter if it looks like a version tag
            if (!string.IsNullOrEmpty(config.Analysis.FromCommit))
            {
                var fromVersion = ExtractVersionFromTag(config.Analysis.FromCommit);
                if (!string.IsNullOrEmpty(fromVersion))
                {
                    return fromVersion;
                }
            }

            // Default to 1.0.0 for day-1 scenarios
            return "1.0.0";
        }

        private string? ExtractVersionFromCommit(string commit)
        {
            // This would typically involve checking git tags associated with the commit
            // For now, return null to use default logic
            return null;
        }

        private string? ExtractVersionFromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return null;

            // Remove common prefixes
            var version = tag;
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                version = version[1..];
            if (version.StartsWith("semver/", StringComparison.OrdinalIgnoreCase))
                version = version[7..];
            if (version.StartsWith("marketing/", StringComparison.OrdinalIgnoreCase))
                version = version[10..];

            // Validate semantic version format
            if (IsValidSemanticVersion(version))
            {
                return version;
            }

            return null;
        }

        private bool IsValidSemanticVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
                return false;

            var parts = version.Split('.');
            return parts.Length >= 3 && parts.Take(3).All(part => int.TryParse(part, out _));
        }

        private VersionIncrement CalculateVersionIncrement(LanguageAnalysisResult languageAnalysis)
        {
            var increment = new VersionIncrement();

            // Rule 1: Entity changes drive major version bumps
            var majorChanges = languageAnalysis.EntityChanges.Where(c => c.IsMajorChange).ToList();
            if (majorChanges.Any())
            {
                increment.Major = 1;
                _logger.LogDebug("Major version increment due to {Count} breaking entity changes", majorChanges.Count);
                return increment; // Major changes reset minor and patch
            }

            // Rule 2: New functionality drives minor version increments
            var minorChanges = 0;
            minorChanges += languageAnalysis.QuantitativeChanges.NewClasses;
            minorChanges += languageAnalysis.QuantitativeChanges.NewMethods;

            // Only count new properties as minor if they're not breaking changes
            var nonBreakingPropertyChanges = languageAnalysis.EntityChanges
                .Where(c => c.Type == "NewProperty" && !c.IsMajorChange)
                .Count();
            minorChanges += nonBreakingPropertyChanges;

            if (minorChanges > 0)
            {
                increment.Minor = minorChanges;
                _logger.LogDebug("Minor version increment: {Count} new features", minorChanges);
            }

            // Rule 3: Bug fixes and improvements drive patch version increments
            var patchChanges = 0;
            patchChanges += languageAnalysis.QuantitativeChanges.BugFixes;
            patchChanges += languageAnalysis.QuantitativeChanges.PerformanceImprovements;
            patchChanges += languageAnalysis.QuantitativeChanges.DocumentationUpdates;

            if (patchChanges > 0)
            {
                increment.Patch = patchChanges;
                _logger.LogDebug("Patch version increment: {Count} fixes and improvements", patchChanges);
            }

            return increment;
        }

        private string ApplyVersionIncrement(string currentVersion, VersionIncrement increment)
        {
            var version = ParseVersion(currentVersion);

            if (increment.Major > 0)
            {
                // Major increment resets minor and patch
                return $"{version.Major + increment.Major}.0.0";
            }
            else if (increment.Minor > 0)
            {
                // Minor increment resets patch
                return $"{version.Major}.{version.Minor + increment.Minor}.0";
            }
            else if (increment.Patch > 0)
            {
                // Patch increment
                return $"{version.Major}.{version.Minor}.{version.Patch + increment.Patch}";
            }
            else
            {
                // No changes
                return currentVersion;
            }
        }

        private string CalculateMarketingVersion(string currentVersion, LanguageAnalysisResult languageAnalysis)
        {
            var version = ParseVersion(currentVersion);

            // Marketing version reflects total volume of changes for business impact
            var totalMinorChanges = languageAnalysis.QuantitativeChanges.NewClasses +
                                  languageAnalysis.QuantitativeChanges.NewMethods +
                                  languageAnalysis.QuantitativeChanges.NewProperties;

            var totalPatchChanges = languageAnalysis.QuantitativeChanges.BugFixes +
                                  languageAnalysis.QuantitativeChanges.PerformanceImprovements +
                                  languageAnalysis.QuantitativeChanges.DocumentationUpdates;

            // Check for major changes
            var hasMajorChanges = languageAnalysis.EntityChanges.Any(c => c.IsMajorChange);

            if (hasMajorChanges)
            {
                // Major changes increment major version
                return $"{version.Major + 1}.0.0";
            }
            else if (totalMinorChanges > 0)
            {
                // Add volume to minor version for marketing impact
                var newMinor = version.Minor + Math.Max(1, totalMinorChanges / 3); // Scale down for marketing
                return $"{version.Major}.{newMinor}.{version.Patch + totalPatchChanges}";
            }
            else if (totalPatchChanges > 0)
            {
                // Just patch changes
                return $"{version.Major}.{version.Minor}.{version.Patch + totalPatchChanges}";
            }
            else
            {
                // No changes
                return currentVersion;
            }
        }

        private string GenerateReasoning(LanguageAnalysisResult languageAnalysis, VersionIncrement increment)
        {
            var reasons = new List<string>();

            // Major version reasoning
            var majorChanges = languageAnalysis.EntityChanges.Where(c => c.IsMajorChange).ToList();
            if (majorChanges.Any())
            {
                var changeTypes = majorChanges.GroupBy(c => c.Type)
                    .Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}")
                    .ToList();

                reasons.Add($"Breaking changes detected: {string.Join(", ", changeTypes)}");
            }

            // Minor version reasoning
            var minorTotal = languageAnalysis.QuantitativeChanges.NewClasses +
                            languageAnalysis.QuantitativeChanges.NewMethods +
                            languageAnalysis.QuantitativeChanges.NewProperties;
            if (minorTotal > 0)
            {
                var details = new List<string>();
                if (languageAnalysis.QuantitativeChanges.NewClasses > 0)
                    details.Add($"{languageAnalysis.QuantitativeChanges.NewClasses} new classes");
                if (languageAnalysis.QuantitativeChanges.NewMethods > 0)
                    details.Add($"{languageAnalysis.QuantitativeChanges.NewMethods} new methods");
                if (languageAnalysis.QuantitativeChanges.NewProperties > 0)
                    details.Add($"{languageAnalysis.QuantitativeChanges.NewProperties} new properties");

                reasons.Add($"New functionality added: {string.Join(", ", details)}");
            }

            // Patch version reasoning
            var patchTotal = languageAnalysis.QuantitativeChanges.BugFixes +
                            languageAnalysis.QuantitativeChanges.PerformanceImprovements +
                            languageAnalysis.QuantitativeChanges.DocumentationUpdates;
            if (patchTotal > 0)
            {
                var details = new List<string>();
                if (languageAnalysis.QuantitativeChanges.BugFixes > 0)
                    details.Add($"{languageAnalysis.QuantitativeChanges.BugFixes} bug fixes");
                if (languageAnalysis.QuantitativeChanges.PerformanceImprovements > 0)
                    details.Add($"{languageAnalysis.QuantitativeChanges.PerformanceImprovements} performance improvements");
                if (languageAnalysis.QuantitativeChanges.DocumentationUpdates > 0)
                    details.Add($"{languageAnalysis.QuantitativeChanges.DocumentationUpdates} documentation updates");

                reasons.Add($"Improvements made: {string.Join(", ", details)}");
            }

            // No changes
            if (!reasons.Any())
            {
                reasons.Add("No significant changes detected");
            }

            return string.Join(". ", reasons);
        }

        private SemanticVersion ParseVersion(string version)
        {
            try
            {
                var parts = version.Split('.');
                var major = parts.Length > 0 ? int.Parse(parts[0]) : 1;
                var minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                var patch = parts.Length > 2 ? int.Parse(parts[2]) : 0;

                return new SemanticVersion(major, minor, patch);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse version: {Version}, using 1.0.0", version);
                return new SemanticVersion(1, 0, 0);
            }
        }

        private class VersionIncrement
        {
            public int Major { get; set; }
            public int Minor { get; set; }
            public int Patch { get; set; }
        }

        private record SemanticVersion(int Major, int Minor, int Patch);
    }
}