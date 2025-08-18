using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Discovery;
using x3squaredcircles.MobileAdapter.Generator.Generation;
using x3squaredcircles.MobileAdapter.Generator.Licensing;
using x3squaredcircles.MobileAdapter.Generator.Models;
using x3squaredcircles.MobileAdapter.Generator.Services;
using x3squaredcircles.MobileAdapter.Generator.TypeMapping;

namespace x3squaredcircles.MobileAdapter.Generator.Core
{
    /// <summary>
    /// The main orchestrator for the mobile adapter generation process.
    /// This engine coordinates the discovery, mapping, and code generation phases.
    /// </summary>
    public class AdapterGeneratorEngine
    {
        private readonly ILogger<AdapterGeneratorEngine> _logger;
        private readonly GeneratorConfiguration _config;
        private readonly ConfigurationValidator _validator;
        private readonly LicenseManager _licenseManager;
        private readonly IClassDiscoveryEngineFactory _discoveryEngineFactory;
        private readonly TypeMappingEngine _typeMappingEngine;
        private readonly ICodeGeneratorFactory _codeGeneratorFactory;
        private readonly ITagTemplateService _tagTemplateService;
        private readonly IFileOutputService _fileOutputService;

        public AdapterGeneratorEngine(
            ILogger<AdapterGeneratorEngine> logger,
            GeneratorConfiguration config,
            ConfigurationValidator validator,
            LicenseManager licenseManager,
            IClassDiscoveryEngineFactory discoveryEngineFactory,
            TypeMappingEngine typeMappingEngine,
            ICodeGeneratorFactory codeGeneratorFactory,
            ITagTemplateService tagTemplateService,
            IFileOutputService fileOutputService)
        {
            _logger = logger;
            _config = config;
            _validator = validator;
            _licenseManager = licenseManager;
            _discoveryEngineFactory = discoveryEngineFactory;
            _typeMappingEngine = typeMappingEngine;
            _codeGeneratorFactory = codeGeneratorFactory;
            _tagTemplateService = tagTemplateService;
            _fileOutputService = fileOutputService;
        }

        /// <summary>
        /// Executes the end-to-end adapter generation workflow.
        /// </summary>
        /// <returns>A GenerationResult object summarizing the outcome of the process.</returns>
        public async Task<Models.GenerationResult> GenerateAdaptersAsync()
        {
            var result = new Models.GenerationResult();
            try
            {
                // Step 0: Validate Configuration
                _logger.LogInformation("Validating configuration...");
                var validationResult = _validator.Validate(_config);
                if (!validationResult.IsValid)
                {
                    validationResult.Errors.ForEach(error => _logger.LogError(error));
                    throw new MobileAdapterException(MobileAdapterExitCode.InvalidConfiguration, "Configuration validation failed.");
                }
                _logger.LogInformation("✓ Configuration validated successfully.");
                if (_config.Verbose) LogConfigurationSummary();

                // Step 1: Validate License
                _logger.LogInformation("Step 1/5: Validating license...");
                var licenseResult = await _licenseManager.ValidateLicenseAsync();
                if (!licenseResult.IsValid)
                {
                    throw new MobileAdapterException(licenseResult.IsExpired ? MobileAdapterExitCode.LicenseExpired : MobileAdapterExitCode.LicenseValidationFailure, licenseResult.ErrorMessage);
                }
                if (licenseResult.IsNoOpMode)
                {
                    _logger.LogWarning("License expired. Running in analysis-only mode.");
                    _config.Mode = OperationMode.Analyze;
                }
                _logger.LogInformation("✓ License validated.");

                // Step 2: Discover Classes
                _logger.LogInformation("Step 2/5: Discovering classes for adapter generation...");
                var discoveryEngine = _discoveryEngineFactory.Create();
                result.DiscoveredClasses = await discoveryEngine.DiscoverClassesAsync(_config);
                if (result.DiscoveredClasses.Count == 0)
                {
                    _logger.LogWarning("No classes found matching the specified discovery criteria. Nothing to generate.");
                    result.Success = true;
                    result.ExitCode = MobileAdapterExitCode.Success;
                    return result;
                }
                _logger.LogInformation("✓ Discovered {ClassCount} classes.", result.DiscoveredClasses.Count);

                // Step 3: Analyze Type Mappings
                _logger.LogInformation("Step 3/5: Analyzing type mappings...");
                result.TypeMappings = await _typeMappingEngine.AnalyzeTypeMappingsAsync(result.DiscoveredClasses, _config);
                _logger.LogInformation("✓ Type mapping analysis complete.");

                // Step 4: Code Generation
                if (_config.Mode == OperationMode.Analyze || _config.DryRun)
                {
                    _logger.LogInformation("Analysis complete. Skipping code generation due to operational mode.");
                    result.Success = true;
                    result.ExitCode = MobileAdapterExitCode.Success;
                }
                else
                {
                    _logger.LogInformation("Step 4/5: Generating adapter code...");
                    var codeGenerator = _codeGeneratorFactory.Create();
                    result.GeneratedFiles = await codeGenerator.GenerateAdaptersAsync(result.DiscoveredClasses, result.TypeMappings, _config);
                    if (result.GeneratedFiles.Count == 0)
                    {
                        throw new MobileAdapterException(MobileAdapterExitCode.GenerationFailure, "Code generation phase completed but produced no files.");
                    }
                    _logger.LogInformation("✓ Code generation complete.");
                }

                // Step 5: Generate Reports and Manifests
                _logger.LogInformation("Step 5/5: Generating reports and manifests...");
                var tagResult = await _tagTemplateService.GenerateTagAsync();
                await _fileOutputService.GenerateOutputsAsync(result, tagResult);
                _logger.LogInformation("✓ Reports and manifests generated.");

                result.Success = true;
                result.ExitCode = MobileAdapterExitCode.Success;
                return result;
            }
            catch (MobileAdapterException ex)
            {
                _logger.LogError(ex, "Adapter generation failed with a known error.");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.ExitCode = ex.ExitCode;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "An unexpected error terminated the generation process.");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.ExitCode = MobileAdapterExitCode.UnhandledException;
                return result;
            }
        }

        private void LogConfigurationSummary()
        {
            _logger.LogDebug("=== Configuration Summary ===");
            _logger.LogDebug("Language: {Language}", _config.GetSelectedLanguage());
            _logger.LogDebug("Platform: {Platform}", _config.GetSelectedPlatform());
            _logger.LogDebug("Repository: {RepoUrl}", _config.RepoUrl);
            _logger.LogDebug("Branch: {Branch}", _config.Branch);
            _logger.LogDebug("Mode: {Mode}", _config.Mode);
            _logger.LogDebug("==============================");
        }
    }
}