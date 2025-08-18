using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IDesignTokenOrchestrator
    {
        Task<int> RunAsync();
    }

    public class DesignTokenOrchestrator : IDesignTokenOrchestrator
    {
        private readonly IConfigurationService _configurationService;
        private readonly ILicenseClientService _licenseClientService;
        private readonly IKeyVaultService _keyVaultService;
        private readonly IGitOperationsService _gitOperationsService;
        private readonly ITokenExtractionService _tokenExtractionService;
        private readonly IPlatformGeneratorFactory _platformGeneratorFactory;
        private readonly ICustomSectionService _customSectionService;
        private readonly IFileOutputService _fileOutputService;
        private readonly ITagTemplateService _tagTemplateService;
        private readonly ILogger<DesignTokenOrchestrator> _logger;

        public DesignTokenOrchestrator(
            IConfigurationService configurationService,
            ILicenseClientService licenseClientService,
            IKeyVaultService keyVaultService,
            IGitOperationsService gitOperationsService,
            ITokenExtractionService tokenExtractionService,
            IPlatformGeneratorFactory platformGeneratorFactory,
            ICustomSectionService customSectionService,
            IFileOutputService fileOutputService,
            ITagTemplateService tagTemplateService,
            ILogger<DesignTokenOrchestrator> logger)
        {
            _configurationService = configurationService;
            _licenseClientService = licenseClientService;
            _keyVaultService = keyVaultService;
            _gitOperationsService = gitOperationsService;
            _tokenExtractionService = tokenExtractionService;
            _platformGeneratorFactory = platformGeneratorFactory;
            _customSectionService = customSectionService;
            _fileOutputService = fileOutputService;
            _tagTemplateService = tagTemplateService;
            _logger = logger;
        }

        public async Task<int> RunAsync()
        {
            DesignTokenConfiguration? config = null;
            LicenseSession? licenseSession = null;
            CancellationTokenSource? heartbeatCancellation = null;

            try
            {
                _logger.LogInformation("🎨 Design Token Generator starting...");

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
                _logger.LogInformation("Step 1/8: Acquiring license...");
                licenseSession = await _licenseClientService.AcquireLicenseAsync(config);

                if (licenseSession == null)
                {
                    _logger.LogError("Failed to acquire license - entering NOOP mode");
                    return await RunNoOpModeAsync(config);
                }

                // 4. Start license heartbeat
                heartbeatCancellation = new CancellationTokenSource();
                _ = Task.Run(() => _licenseClientService.StartHeartbeatAsync(licenseSession, heartbeatCancellation.Token));

                // 5. Validate git repository and configure authentication
                _logger.LogInformation("Step 2/8: Configuring git operations...");
                await ValidateAndConfigureGitAsync(config);

                // 6. Extract and process design tokens
                _logger.LogInformation("Step 3/8: Extracting design tokens from {Platform}...",
                    config.DesignPlatform.GetSelectedPlatform().ToUpperInvariant());
                var extractedTokens = await _tokenExtractionService.ExtractAndProcessTokensAsync(config);

                // 7. Check for design changes (if not in validate-only mode)
                if (!config.Operation.ValidateOnly)
                {
                    _logger.LogInformation("Step 4/8: Checking for design changes...");
                    var hasChanges = await _tokenExtractionService.HasDesignChangesAsync(
                        extractedTokens,
                        config.FileManagement.OutputDir);

                    if (!hasChanges && config.Operation.Mode == "sync")
                    {
                        _logger.LogInformation("No design changes detected - skipping generation");
                        return (int)DesignTokenExitCode.NoDesignChangesDetected;
                    }
                }

                // 8. Generate platform-specific files
                _logger.LogInformation("Step 5/8: Generating {Platform} files...",
                    config.TargetPlatform.GetSelectedPlatform().ToUpperInvariant());
                var generationResult = await GeneratePlatformFilesAsync(config, extractedTokens);

                // 9. Handle custom sections if needed
                if (config.Operation.PreserveCustom && generationResult.Success)
                {
                    _logger.LogInformation("Step 6/8: Processing custom sections...");
                    await ProcessCustomSectionsAsync(config, generationResult);
                }

                // 10. Generate tag template
                _logger.LogInformation("Step 7/8: Generating git tags...");
                var tagResult = await _tagTemplateService.GenerateTagAsync(config, extractedTokens);

                // 11. Generate outputs and reports
                _logger.LogInformation("Step 8/8: Generating output files...");
                await _fileOutputService.GenerateOutputsAsync(config, extractedTokens, generationResult, tagResult, licenseSession);

                // 12. Apply git operations if in sync mode
                if (config.Operation.Mode == "sync" && !config.Operation.ValidateOnly && !config.Operation.NoOp)
                {
                    await ApplyGitOperationsAsync(config, generationResult, tagResult);
                }

                // 13. Display results
                DisplayResults(config, extractedTokens, generationResult, tagResult, licenseSession);

                _logger.LogInformation("✅ Design Token Generator completed successfully");
                return (int)DesignTokenExitCode.Success;
            }
            catch (DesignTokenException ex)
            {
                _logger.LogError("❌ Design Token Generator failed: {Message}", ex.Message);
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in Design Token Generator");
                return (int)DesignTokenExitCode.InvalidConfiguration;
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

        private async Task<int> RunNoOpModeAsync(DesignTokenConfiguration config)
        {
            try
            {
                _logger.LogWarning("⚠️ Running in NO-OP mode due to license unavailability");
                _logger.LogInformation("Analysis will be performed but no changes will be applied");

                // Perform analysis without license
                await ValidateAndConfigureGitAsync(config);
                var extractedTokens = await _tokenExtractionService.ExtractAndProcessTokensAsync(config);
                var generationResult = await GeneratePlatformFilesAsync(config, extractedTokens);
                var tagResult = await _tagTemplateService.GenerateTagAsync(config, extractedTokens);

                // Generate outputs but mark as NOOP
                await _fileOutputService.GenerateOutputsAsync(config, extractedTokens, generationResult, tagResult, null);

                // Display results with NOOP warning
                DisplayNoOpResults(config, extractedTokens, generationResult);

                _logger.LogWarning("✅ Design Token Generator completed in NO-OP mode");
                return (int)DesignTokenExitCode.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during NO-OP analysis");
                return (int)DesignTokenExitCode.InvalidConfiguration;
            }
        }

        private async Task ValidateAndConfigureGitAsync(DesignTokenConfiguration config)
        {
            var isValidRepo = await _gitOperationsService.IsValidGitRepositoryAsync();
            if (!isValidRepo)
            {
                throw new DesignTokenException(DesignTokenExitCode.RepositoryAccessFailure,
                    "Not a valid git repository. Ensure the tool is running in a git repository.");
            }

            await _gitOperationsService.ConfigureGitAuthenticationAsync(config);
            _logger.LogInformation("✓ Git repository validated and configured");
        }

        private async Task<GenerationResult> GeneratePlatformFilesAsync(DesignTokenConfiguration config, TokenCollection tokens)
        {
            var generationRequest = new GenerationRequest
            {
                Tokens = tokens,
                Platform = config.TargetPlatform,
                OutputDirectory = config.TargetPlatform.GetSelectedPlatform() switch
                {
                    "android" => config.TargetPlatform.AndroidOutputDir,
                    "ios" => config.TargetPlatform.IosOutputDir,
                    "web" => config.TargetPlatform.WebOutputDir,
                    _ => throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration,
                        $"Unsupported target platform: {config.TargetPlatform.GetSelectedPlatform()}")
                }
            };

            var result = await _platformGeneratorFactory.GenerateAsync(generationRequest);

            if (!result.Success)
            {
                throw new DesignTokenException(DesignTokenExitCode.PlatformGenerationFailure,
                    result.ErrorMessage ?? "Platform file generation failed");
            }

            return result;
        }

        private async Task ProcessCustomSectionsAsync(DesignTokenConfiguration config, GenerationResult generationResult)
        {
            foreach (var file in generationResult.Files)
            {
                if (file.HasCustomSections)
                {
                    _logger.LogDebug("Processing custom sections for: {FilePath}", file.FilePath);

                    // Check for conflicts
                    var hasConflicts = await _customSectionService.HasCustomSectionConflictsAsync(
                        file.PreservedSections,
                        new List<CustomSection>());

                    if (hasConflicts && config.Operation.MergeStrategy == "prompt")
                    {
                        _logger.LogWarning("Custom section conflicts detected in: {FilePath}", file.FilePath);
                    }
                }
            }

            // Save custom sections inventory
            await _customSectionService.SaveCustomSectionsInventoryAsync(
                generationResult.Files,
                config.FileManagement.OutputDir);
        }

        private async Task ApplyGitOperationsAsync(DesignTokenConfiguration config, GenerationResult generationResult, TagTemplateResult tagResult)
        {
            try
            {
                _logger.LogInformation("Applying git operations...");

                // Create branch if requested
                if (config.Git.CreateBranch && !string.IsNullOrEmpty(config.Git.BranchNameTemplate))
                {
                    var branchName = ProcessBranchNameTemplate(config.Git.BranchNameTemplate, tagResult.TokenValues);
                    await _gitOperationsService.CreateBranchAsync(branchName);
                }

                // Commit changes if requested
                if (config.Git.AutoCommit)
                {
                    var commitMessage = config.Git.CommitMessage ??
                        $"feat: update {config.TargetPlatform.GetSelectedPlatform()} tokens from {config.DesignPlatform.GetSelectedPlatform()}";

                    var success = await _gitOperationsService.CommitChangesAsync(
                        commitMessage,
                        config.Git.CommitAuthorName,
                        config.Git.CommitAuthorEmail);

                    if (success)
                    {
                        // Create git tag
                        await _gitOperationsService.CreateTagAsync(
                            tagResult.GeneratedTag,
                            $"Design tokens v{tagResult.TokenValues.GetValueOrDefault("version", "1.0.0")}");

                        // Push changes if we're on main/master branch
                        if (config.Branch.ToLowerInvariant() is "main" or "master")
                        {
                            await _gitOperationsService.PushChangesAsync();
                        }
                    }
                }

                // Create pull request if requested
                if (config.Git.CreatePullRequest && !string.IsNullOrEmpty(config.Git.BranchNameTemplate))
                {
                    var sourceBranch = ProcessBranchNameTemplate(config.Git.BranchNameTemplate, tagResult.TokenValues);
                    var targetBranch = config.Branch;
                    var title = $"Update {config.TargetPlatform.GetSelectedPlatform()} design tokens";
                    var description = $"Automated update of design tokens from {config.DesignPlatform.GetSelectedPlatform()}";

                    await _gitOperationsService.CreatePullRequestAsync(sourceBranch, targetBranch, title, description);
                }

                _logger.LogInformation("✓ Git operations completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Git operations failed");
                throw new DesignTokenException(DesignTokenExitCode.GitOperationFailure,
                    $"Git operations failed: {ex.Message}", ex);
            }
        }

        private string ProcessBranchNameTemplate(string template, Dictionary<string, string> tokenValues)
        {
            var result = template;
            foreach (var token in tokenValues)
            {
                result = result.Replace($"{{{token.Key}}}", token.Value, StringComparison.OrdinalIgnoreCase);
            }
            return result;
        }

        private void DisplayResults(
            DesignTokenConfiguration config,
            TokenCollection extractedTokens,
            GenerationResult generationResult,
            TagTemplateResult tagResult,
            LicenseSession licenseSession)
        {
            Console.WriteLine();
            Console.WriteLine("🎨 Design Token Generator Results");
            Console.WriteLine();

            Console.WriteLine($"Design Platform:    {config.DesignPlatform.GetSelectedPlatform().ToUpperInvariant()}");
            Console.WriteLine($"Target Platform:    {config.TargetPlatform.GetSelectedPlatform().ToUpperInvariant()}");
            Console.WriteLine($"Tokens Extracted:   {extractedTokens.Tokens.Count}");
            Console.WriteLine($"Files Generated:    {generationResult.Files.Count}");
            Console.WriteLine($"Generated Tag:      {tagResult.GeneratedTag}");
            Console.WriteLine();

            if (generationResult.Files.Any(f => f.HasCustomSections))
            {
                var customSectionCount = generationResult.Files.Sum(f => f.PreservedSections.Count);
                Console.WriteLine($"🔧 CUSTOM SECTIONS: {customSectionCount} custom sections preserved");
                foreach (var file in generationResult.Files.Where(f => f.HasCustomSections))
                {
                    Console.WriteLine($"  - {Path.GetFileName(file.FilePath)}: {file.PreservedSections.Count} sections");
                }
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:     {config.RepoUrl}");
            Console.WriteLine($"Branch:         {config.Branch}");
            Console.WriteLine($"Mode:           {config.Operation.Mode.ToUpperInvariant()}");
            Console.WriteLine($"Output Dir:     {config.FileManagement.OutputDir}");

            if (licenseSession?.BurstMode == true)
            {
                Console.WriteLine();
                Console.WriteLine("⚠️ BURST MODE USAGE NOTICE ⚠️");
                Console.WriteLine($"This generation used burst capacity ({licenseSession.BurstCountRemaining} remaining this month)");
                Console.WriteLine("Consider purchasing additional licenses to avoid future interruptions");
            }
        }

        private void DisplayNoOpResults(
            DesignTokenConfiguration config,
            TokenCollection extractedTokens,
            GenerationResult generationResult)
        {
            Console.WriteLine();
            Console.WriteLine("🎨 Design Token Generator Results (NO-OP MODE)");
            Console.WriteLine("   ⚠️ License unavailable - analysis only, no changes applied");
            Console.WriteLine();

            Console.WriteLine($"Design Platform:    {config.DesignPlatform.GetSelectedPlatform().ToUpperInvariant()}");
            Console.WriteLine($"Target Platform:    {config.TargetPlatform.GetSelectedPlatform().ToUpperInvariant()}");
            Console.WriteLine($"Tokens Analyzed:    {extractedTokens.Tokens.Count}");
            Console.WriteLine($"Files Analyzed:     {generationResult.Files.Count}");
            Console.WriteLine();

            Console.WriteLine("⚠️ NO CHANGES WERE APPLIED ⚠️");
            Console.WriteLine("License server was unavailable - pipeline analysis completed but");
            Console.WriteLine("no files were written and no git operations were performed.");
            Console.WriteLine();
        }
    }
}