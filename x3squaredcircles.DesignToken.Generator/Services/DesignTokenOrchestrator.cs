using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IDesignTokenOrchestrator
    {
        Task<DesignTokenExitCode> RunAsync();
    }

    public class DesignTokenOrchestrator : IDesignTokenOrchestrator
    {
        private readonly IConfigurationService _configurationService;
        private readonly ILicenseClientService _licenseClientService;
        private readonly IKeyVaultService _keyVaultService;
        private readonly IGitOperationsService _gitOperationsService;
        private readonly ITokenExtractionService _tokenExtractionService;
        private readonly IPlatformGeneratorFactory _platformGeneratorFactory;
        private readonly IFileOutputService _fileOutputService;
        private readonly ITagTemplateService _tagTemplateService;
        private readonly IControlPointService _controlPointService;
        private readonly IAppLogger _logger;

        private TokensConfiguration _config = new();

        public DesignTokenOrchestrator(
            IConfigurationService configurationService,
            ILicenseClientService licenseClientService,
            IKeyVaultService keyVaultService,
            IGitOperationsService gitOperationsService,
            ITokenExtractionService tokenExtractionService,
            IPlatformGeneratorFactory platformGeneratorFactory,
            IFileOutputService fileOutputService,
            ITagTemplateService tagTemplateService,
            IControlPointService controlPointService,
            IAppLogger logger)
        {
            _configurationService = configurationService;
            _licenseClientService = licenseClientService;
            _keyVaultService = keyVaultService;
            _gitOperationsService = gitOperationsService;
            _tokenExtractionService = tokenExtractionService;
            _platformGeneratorFactory = platformGeneratorFactory;
            _fileOutputService = fileOutputService;
            _tagTemplateService = tagTemplateService;
            _controlPointService = controlPointService;
            _logger = logger;
        }

        public async Task<DesignTokenExitCode> RunAsync()
        {
            LicenseSession? licenseSession = null;
            var heartbeatCancellation = new CancellationTokenSource();

            try
            {
                await PreflightAsync();
                licenseSession = await AcquireLicenseAsync(heartbeatCancellation.Token);
                var extractedTokens = await ExtractTokensAsync();

                if (!_config.ValidateOnly)
                {
                    var hasChanges = await _tokenExtractionService.HasDesignChangesAsync(extractedTokens, _config);
                    if (!hasChanges)
                    {
                        _logger.LogInfo("No design changes detected. Exiting successfully.");
                        await _controlPointService.InvokeAsync(ControlPointStage.RunEnd, "OnSuccess", payloadMetadata: new() { ["reason"] = "NoChangesDetected" });
                        return DesignTokenExitCode.Success;
                    }
                }

                var generationResult = await GeneratePlatformFilesAsync(extractedTokens);

                //
                // THE FIX IS HERE: The call to GenerateOutputsAsync has been updated.
                // It now correctly calls the single high-level method on the FileOutputService.
                //
                await GenerateOutputFilesAsync(extractedTokens, generationResult, licenseSession);

                _logger.LogInfo("✅ Design Token Generator completed successfully");
                await _controlPointService.InvokeAsync(ControlPointStage.RunEnd, "OnSuccess");
                return DesignTokenExitCode.Success;
            }
            catch (DesignTokenException ex)
            {
                _logger.LogError($"A known error occurred: {ex.Message}", ex);
                await _controlPointService.InvokeAsync(ControlPointStage.RunEnd, "OnFailure", payloadMetadata: new() { ["error"] = ex.Message });
                return ex.ExitCode;
            }
            catch (Exception ex)
            {
                _logger.LogCritical("An unexpected fatal error occurred in the orchestrator.", ex);
                await _controlPointService.InvokeAsync(ControlPointStage.RunEnd, "OnFailure", payloadMetadata: new() { ["error"] = ex.ToString() });
                return DesignTokenExitCode.UnhandledException;
            }
            finally
            {
                heartbeatCancellation.Cancel();
                if (licenseSession != null)
                {
                    await _licenseClientService.ReleaseLicenseAsync(licenseSession);
                }
            }
        }

        #region Private Orchestration Stages

        private async Task PreflightAsync()
        {
            _logger.LogStartPhase("Preflight");
            await _controlPointService.InvokeAsync(ControlPointStage.RunStart, "OnRunStart", isBlocking: true);

            _config = _configurationService.GetConfiguration();
            _configurationService.ValidateConfiguration(_config);
            _configurationService.LogConfiguration(_config, _logger);

            if (!string.IsNullOrEmpty(_config.KeyVault.Type))
            {
                await _keyVaultService.ResolveSecretsAsync(_config);
            }

            var isValidRepo = await _gitOperationsService.IsValidGitRepositoryAsync();
            if (!isValidRepo)
            {
                throw new DesignTokenException(DesignTokenExitCode.RepositoryAccessFailure, "Not a valid git repository. Ensure the tool is running in a git repository.");
            }
            await _gitOperationsService.ConfigureGitAuthenticationAsync(_config);

            _logger.LogEndPhase("Preflight", true);
        }

        private async Task<LicenseSession> AcquireLicenseAsync(CancellationToken cancellationToken)
        {
            _logger.LogStartPhase("License Acquisition");
            var licenseSession = await _licenseClientService.AcquireLicenseAsync(_config);
            if (licenseSession == null)
            {
                throw new DesignTokenException(DesignTokenExitCode.LicenseUnavailable, "Failed to acquire a valid license session.");
            }
            _ = Task.Run(() => _licenseClientService.StartHeartbeatAsync(licenseSession, cancellationToken), cancellationToken);
            _logger.LogEndPhase("License Acquisition", true);
            return licenseSession;
        }

        private async Task<TokenCollection> ExtractTokensAsync()
        {
            _logger.LogStartPhase("Token Extraction & Normalization");
            TokenCollection? extractedTokens;
            try
            {
                extractedTokens = await _tokenExtractionService.ExtractAndProcessTokensAsync(_config);
                await _controlPointService.InvokeAsync(ControlPointStage.Extract, "OnSuccess", payloadMetadata: new()
                {
                    ["source"] = extractedTokens.Source,
                    ["tokenCount"] = extractedTokens.Tokens.Count
                });
                _logger.LogEndPhase("Token Extraction & Normalization", true);
                return extractedTokens;
            }
            catch (Exception ex)
            {
                var message = ex is DesignTokenException de ? de.Message : ex.ToString();
                await _controlPointService.InvokeAsync(ControlPointStage.Extract, "OnFailure", payloadMetadata: new() { ["error"] = message });
                throw;
            }
        }

        private async Task<GenerationResult> GeneratePlatformFilesAsync(TokenCollection tokens)
        {
            _logger.LogStartPhase("Platform File Generation");
            GenerationResult? generationResult;
            try
            {
                var generationRequest = new GenerationRequest
                {
                    Tokens = tokens,
                    OutputDirectory = _config.TargetPlatform switch
                    {
                        "android" => _config.Android.OutputDir,
                        "ios" => _config.Ios.OutputDir,
                        "web" => _config.Web.OutputDir,
                        _ => throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration, $"Unsupported target platform: {_config.TargetPlatform}")
                    }
                };

                //
                // THE FIX IS HERE: The call to GenerateAsync now correctly passes the 'config' object.
                //
                generationResult = await _platformGeneratorFactory.GenerateAsync(generationRequest, _config);
                if (!generationResult.Success)
                {
                    throw new DesignTokenException(DesignTokenExitCode.PlatformGenerationFailure, generationResult.ErrorMessage ?? "Platform file generation failed");
                }

                await _controlPointService.InvokeAsync(ControlPointStage.Generate, "OnSuccess", payloadMetadata: new()
                {
                    ["filesGenerated"] = generationResult.Files.Count,
                    ["outputDirectory"] = generationRequest.OutputDirectory
                });
                _logger.LogEndPhase("Platform File Generation", true);
                return generationResult;
            }
            catch (Exception ex)
            {
                var message = ex is DesignTokenException de ? de.Message : ex.ToString();
                await _controlPointService.InvokeAsync(ControlPointStage.Generate, "OnFailure", payloadMetadata: new() { ["error"] = message });
                throw;
            }
        }

        // --- NEW: Method to encapsulate file output and git logic ---
        private async Task GenerateOutputFilesAsync(TokenCollection tokens, GenerationResult generationResult, LicenseSession? licenseSession)
        {
            _logger.LogStartPhase("Output Generation");
            var tagResult = await _tagTemplateService.GenerateTagAsync(_config, tokens);
            await _fileOutputService.GenerateOutputsAsync(_config, tokens, generationResult, tagResult, licenseSession);

            if (_config.Git.AutoCommit && !_config.ValidateOnly && !_config.NoOp)
            {
                await CommitAndPushAsync(tagResult);
            }
            _logger.LogEndPhase("Output Generation", true);
        }

        private async Task CommitAndPushAsync(TagTemplateResult tagResult)
        {
            _logger.LogStartPhase("Git Operations");

            var canCommit = await _controlPointService.InvokeAsync(ControlPointStage.Commit, "BeforeCommit", isBlocking: true);

            if (!canCommit)
            {
                throw new DesignTokenException(DesignTokenExitCode.GitOperationFailure, "Commit aborted by 'BeforeCommit' Control Point.");
            }

            var success = await _gitOperationsService.CommitChangesAsync(_config.Git.CommitMessage);
            if (success)
            {
                var commitHash = await _gitOperationsService.GetCurrentCommitHashAsync() ?? "unknown";
                await _controlPointService.InvokeAsync(ControlPointStage.Commit, "OnSuccess", payloadMetadata: new()
                {
                    ["commitHash"] = commitHash,
                    ["tagCreated"] = tagResult.GeneratedTag
                });

                await _gitOperationsService.CreateTagAsync(tagResult.GeneratedTag, $"Automated design token release v{tagResult.TokenValues.GetValueOrDefault("version", "1.0.0")}");
                await _gitOperationsService.PushChangesAsync("HEAD");
                await _gitOperationsService.PushChangesAsync(tagResult.GeneratedTag);
            }

            _logger.LogEndPhase("Git Operations", true);
        }

        #endregion
    }
}