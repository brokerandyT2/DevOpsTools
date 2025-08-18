using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container.Services
{
    public interface IVersionDetectiveOrchestrator
    {
        Task<int> RunAsync();
    }

    public class VersionDetectiveOrchestrator : IVersionDetectiveOrchestrator
    {
        private readonly IConfigurationService _configurationService;
        private readonly ILicenseClientService _licenseClientService;
        private readonly IGitAnalysisService _gitAnalysisService;
        private readonly ILanguageAnalysisService _languageAnalysisService;
        private readonly IVersionCalculationService _versionCalculationService;
        private readonly ITagTemplateService _tagTemplateService;
        private readonly IOutputService _outputService;
        private readonly IKeyVaultService _keyVaultService;
        private readonly ILogger<VersionDetectiveOrchestrator> _logger;

        public VersionDetectiveOrchestrator(
            IConfigurationService configurationService,
            ILicenseClientService licenseClientService,
            IGitAnalysisService gitAnalysisService,
            ILanguageAnalysisService languageAnalysisService,
            IVersionCalculationService versionCalculationService,
            ITagTemplateService tagTemplateService,
            IOutputService outputService,
            IKeyVaultService keyVaultService,
            ILogger<VersionDetectiveOrchestrator> logger)
        {
            _configurationService = configurationService;
            _licenseClientService = licenseClientService;
            _gitAnalysisService = gitAnalysisService;
            _languageAnalysisService = languageAnalysisService;
            _versionCalculationService = versionCalculationService;
            _tagTemplateService = tagTemplateService;
            _outputService = outputService;
            _keyVaultService = keyVaultService;
            _logger = logger;
        }

        public async Task<int> RunAsync()
        {
            VersionDetectiveConfiguration? config = null;
            LicenseSession? licenseSession = null;
            CancellationTokenSource? heartbeatCancellation = null;

            try
            {
                _logger.LogInformation("🔍 Version Detective Container starting analysis...");

                // 1. Parse and validate configuration
                config = _configurationService.GetConfiguration();
                _configurationService.ValidateConfiguration(config);
                _configurationService.LogConfiguration(config);

                // 2. Resolve secrets from key vault if needed
                if (config.KeyVault != null)
                {
                    await _keyVaultService.ResolveSecretsAsync(config);
                }

                // 3. Acquire license
                _logger.LogInformation("Step 1/7: Acquiring license...");
                licenseSession = await _licenseClientService.AcquireLicenseAsync(config);

                if (licenseSession == null)
                {
                    _logger.LogError("Failed to acquire license - entering NOOP mode");
                    return await RunNoOpModeAsync(config);
                }

                // 4. Start license heartbeat
                heartbeatCancellation = new CancellationTokenSource();
                _ = Task.Run(() => _licenseClientService.StartHeartbeatAsync(licenseSession, heartbeatCancellation.Token));

                // 5. Perform git analysis
                _logger.LogInformation("Step 2/7: Analyzing git repository...");
                var gitAnalysis = await _gitAnalysisService.AnalyzeRepositoryAsync(config);

                // 6. Perform language-specific analysis
                _logger.LogInformation("Step 3/7: Analyzing {Language} code...", config.Language.GetSelectedLanguage().ToUpperInvariant());
                var languageAnalysis = await _languageAnalysisService.AnalyzeCodeAsync(config, gitAnalysis);

                // 7. Calculate version changes
                _logger.LogInformation("Step 4/7: Calculating version impact...");
                var versionResult = await _versionCalculationService.CalculateVersionAsync(config, languageAnalysis, gitAnalysis);

                // 8. Generate tag templates
                _logger.LogInformation("Step 5/7: Generating tag templates...");
                var tagResult = await _tagTemplateService.GenerateTagsAsync(config, versionResult, gitAnalysis);

                // 9. Generate outputs
                _logger.LogInformation("Step 6/7: Generating output files...");
                await _outputService.GenerateOutputsAsync(config, versionResult, tagResult, gitAnalysis, licenseSession);

                // 10. Apply changes if in deploy mode
                if (config.Analysis.Mode == "deploy" && !config.Analysis.ValidateOnly && !config.Analysis.NoOp)
                {
                    _logger.LogInformation("Step 7/7: Applying deployment changes...");
                    await ApplyDeploymentChangesAsync(config, versionResult, tagResult);
                }
                else
                {
                    _logger.LogInformation("Step 7/7: Skipped (analysis-only mode)");
                }

                // 11. Display results
                DisplayResults(config, versionResult, licenseSession);

                _logger.LogInformation("✅ Version Detective completed successfully");
                return (int)VersionDetectiveExitCode.Success;
            }
            catch (VersionDetectiveException ex)
            {
                _logger.LogError("❌ Version Detective failed: {Message}", ex.Message);
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in Version Detective");
                return (int)VersionDetectiveExitCode.InvalidConfiguration;
            }
            finally
            {
                // Clean up
                try
                {
                    heartbeatCancellation?.Cancel();
                    if (licenseSession != null)
                    {
                        await _licenseClientService.ReleaseLicenseAsync(licenseSession);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during cleanup");
                }
            }
        }

        private async Task<int> RunNoOpModeAsync(VersionDetectiveConfiguration config)
        {
            try
            {
                _logger.LogWarning("⚠️ Running in NO-OP mode due to license unavailability");
                _logger.LogInformation("Analysis will be performed but no changes will be applied");

                // Perform analysis without license
                var gitAnalysis = await _gitAnalysisService.AnalyzeRepositoryAsync(config);
                var languageAnalysis = await _languageAnalysisService.AnalyzeCodeAsync(config, gitAnalysis);
                var versionResult = await _versionCalculationService.CalculateVersionAsync(config, languageAnalysis, gitAnalysis);
                var tagResult = await _tagTemplateService.GenerateTagsAsync(config, versionResult, gitAnalysis);

                // Generate outputs but mark as NOOP
                await _outputService.GenerateOutputsAsync(config, versionResult, tagResult, gitAnalysis, null);

                // Display results with NOOP warning
                DisplayNoOpResults(config, versionResult);

                _logger.LogWarning("✅ Version Detective completed in NO-OP mode");
                return (int)VersionDetectiveExitCode.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during NO-OP analysis");
                return (int)VersionDetectiveExitCode.InvalidConfiguration;
            }
        }

        private async Task ApplyDeploymentChangesAsync(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult,
            TagTemplateResult tagResult)
        {
            try
            {
                if (config.Branch.ToLowerInvariant() != "main" && config.Branch.ToLowerInvariant() != "master")
                {
                    _logger.LogInformation("Skipping deployment changes - not on main/master branch");
                    return;
                }

                _logger.LogInformation("Creating git tags...");
                await _gitAnalysisService.CreateTagAsync(tagResult.SemanticTag, $"Semantic version {versionResult.NewSemanticVersion}");
                await _gitAnalysisService.CreateTagAsync(tagResult.MarketingTag, $"Marketing version {versionResult.NewMarketingVersion}");

                _logger.LogInformation("✓ Deployment changes applied successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply deployment changes");
                throw;
            }
        }

        private void DisplayResults(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult,
            LicenseSession licenseSession)
        {
            Console.WriteLine();
            Console.WriteLine("🏷️ Version Detective Analysis Results");
            if (config.Analysis.Mode == "pr")
            {
                Console.WriteLine("   (Preliminary - final version determined at deployment)");
            }
            Console.WriteLine();

            Console.WriteLine($"Current Version:    {versionResult.CurrentVersion}");
            Console.WriteLine($"New Semantic:       {versionResult.NewSemanticVersion}");
            Console.WriteLine($"New Marketing:      {versionResult.NewMarketingVersion}");
            Console.WriteLine();

            if (versionResult.HasMajorChanges)
            {
                Console.WriteLine($"🔄 MAJOR: Breaking changes detected ({versionResult.MajorChanges.Count} changes)");
                foreach (var change in versionResult.MajorChanges)
                {
                    Console.WriteLine($"  - {change.Type}: {change.Description}");
                }
                Console.WriteLine();
            }

            if (versionResult.MinorChanges > 0)
            {
                Console.WriteLine($"✨ MINOR: {versionResult.MinorChanges} new features/capabilities added");
                Console.WriteLine();
            }

            if (versionResult.PatchChanges > 0)
            {
                Console.WriteLine($"🐛 PATCH: {versionResult.PatchChanges} improvements and fixes");
                Console.WriteLine();
            }

            Console.WriteLine($"Reasoning: {versionResult.Reasoning}");
            Console.WriteLine();

            if (licenseSession?.BurstMode == true)
            {
                Console.WriteLine("⚠️ BURST MODE USAGE NOTICE ⚠️");
                Console.WriteLine($"This analysis used burst capacity ({licenseSession.BurstCountRemaining} remaining this month)");
                Console.WriteLine("Consider purchasing additional licenses to avoid future interruptions");
                Console.WriteLine();
            }

            Console.WriteLine($"Language:     {config.Language.GetSelectedLanguage().ToUpperInvariant()}");
            Console.WriteLine($"Repository:   {config.RepoUrl}");
            Console.WriteLine($"Branch:       {config.Branch}");
            Console.WriteLine($"Mode:         {config.Analysis.Mode.ToUpperInvariant()}");
        }

        private void DisplayNoOpResults(
            VersionDetectiveConfiguration config,
            VersionCalculationResult versionResult)
        {
            Console.WriteLine();
            Console.WriteLine("🏷️ Version Detective Analysis Results (NO-OP MODE)");
            Console.WriteLine("   ⚠️ License unavailable - analysis only, no changes applied");
            Console.WriteLine();

            Console.WriteLine($"Current Version:    {versionResult.CurrentVersion}");
            Console.WriteLine($"Calculated Semantic: {versionResult.NewSemanticVersion}");
            Console.WriteLine($"Calculated Marketing: {versionResult.NewMarketingVersion}");
            Console.WriteLine();

            Console.WriteLine($"Reasoning: {versionResult.Reasoning}");
            Console.WriteLine();

            Console.WriteLine("⚠️ NO CHANGES WERE APPLIED ⚠️");
            Console.WriteLine("License server was unavailable - pipeline analysis completed but");
            Console.WriteLine("no version tags were created and no deployment actions were taken.");
            Console.WriteLine();
        }
    }
}