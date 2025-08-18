using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Schema;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface ISqlSchemaOrchestrator
    {
        Task<int> RunAsync();
    }

    public class SqlSchemaOrchestrator : ISqlSchemaOrchestrator
    {
        private readonly IConfigurationService _configurationService;
        private readonly ILicenseClientService _licenseClientService;
        private readonly IKeyVaultService _keyVaultService;
        private readonly IGitOperationsService _gitOperationsService;
        private readonly IEntityDiscoveryService _entityDiscoveryService;
        private readonly ISchemaAnalysisService _schemaAnalysisService;
        private readonly ISchemaValidationService _schemaValidationService;
        private readonly IRiskAssessmentService _riskAssessmentService;
        private readonly IDeploymentPlanService _deploymentPlanService;
        private readonly ISqlGenerationService _sqlGenerationService;
        private readonly IBackupService _backupService;
        private readonly IDeploymentExecutionService _deploymentExecutionService;
        private readonly IFileOutputService _fileOutputService;
        private readonly ITagTemplateService _tagTemplateService;
        private readonly ILogger<SqlSchemaOrchestrator> _logger;

        public SqlSchemaOrchestrator(
            IConfigurationService configurationService,
            ILicenseClientService licenseClientService,
            IKeyVaultService keyVaultService,
            IGitOperationsService gitOperationsService,
            IEntityDiscoveryService entityDiscoveryService,
            ISchemaAnalysisService schemaAnalysisService,
            ISchemaValidationService schemaValidationService,
            IRiskAssessmentService riskAssessmentService,
            IDeploymentPlanService deploymentPlanService,
            ISqlGenerationService sqlGenerationService,
            IBackupService backupService,
            IDeploymentExecutionService deploymentExecutionService,
            IFileOutputService fileOutputService,
            ITagTemplateService tagTemplateService,
            ILogger<SqlSchemaOrchestrator> logger)
        {
            _configurationService = configurationService;
            _licenseClientService = licenseClientService;
            _keyVaultService = keyVaultService;
            _gitOperationsService = gitOperationsService;
            _entityDiscoveryService = entityDiscoveryService;
            _schemaAnalysisService = schemaAnalysisService;
            _schemaValidationService = schemaValidationService;
            _riskAssessmentService = riskAssessmentService;
            _deploymentPlanService = deploymentPlanService;
            _sqlGenerationService = sqlGenerationService;
            _backupService = backupService;
            _deploymentExecutionService = deploymentExecutionService;
            _fileOutputService = fileOutputService;
            _tagTemplateService = tagTemplateService;
            _logger = logger;
        }

        public async Task<int> RunAsync()
        {
            SqlSchemaConfiguration? config = null;
            LicenseSession? licenseSession = null;
            CancellationTokenSource? heartbeatCancellation = null;

            try
            {
                _logger.LogInformation("🗄️ SQL Schema Generator starting...");

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
                _logger.LogInformation("Step 1/10: Acquiring license...");
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
                _logger.LogInformation("Step 2/10: Configuring git operations...");
                await ValidateAndConfigureGitAsync(config);

                // 6. Discover entities marked with tracking attribute
                _logger.LogInformation("Step 3/10: Discovering entities with attribute: {TrackAttribute}...", config.TrackAttribute);
                var discoveredEntities = await _entityDiscoveryService.DiscoverEntitiesAsync(config);

                // 7. Analyze current database schema
                _logger.LogInformation("Step 4/10: Analyzing current database schema...");
                var currentSchema = await _schemaAnalysisService.AnalyzeCurrentSchemaAsync(config);

                // 8. Generate target schema from entities
                _logger.LogInformation("Step 5/10: Generating target schema from entities...");
                var targetSchema = await _schemaAnalysisService.GenerateTargetSchemaAsync(discoveredEntities, config);

                // 9. Validate schema changes
                _logger.LogInformation("Step 6/10: Validating schema changes...");
                var validationResult = await _schemaValidationService.ValidateSchemaChangesAsync(currentSchema, targetSchema, config);

                // 10. Assess risk level
                _logger.LogInformation("Step 7/10: Assessing deployment risk...");
                var riskAssessment = await _riskAssessmentService.AssessRiskAsync(validationResult, config);

                // 11. Generate 29-phase deployment plan
                _logger.LogInformation("Step 8/10: Generating deployment plan...");
                var deploymentPlan = await _deploymentPlanService.GenerateDeploymentPlanAsync(validationResult, riskAssessment, config);

                // 12. Generate SQL deployment script
                _logger.LogInformation("Step 9/10: Generating SQL deployment script...");
                var sqlScript = await _sqlGenerationService.GenerateDeploymentScriptAsync(deploymentPlan, config);

                // 13. Generate tag template
                var tagResult = await _tagTemplateService.GenerateTagAsync(config, discoveredEntities);

                // 14. Generate output files and reports
                _logger.LogInformation("Step 10/10: Generating output files...");
                await _fileOutputService.GenerateOutputsAsync(config, discoveredEntities, currentSchema, targetSchema,
                    validationResult, riskAssessment, deploymentPlan, sqlScript, tagResult, licenseSession);

                // 15. Execute deployment if in execute mode
                if (config.Operation.Mode == "execute" && !config.Operation.ValidateOnly && !config.Operation.NoOp)
                {
                    await ExecuteDeploymentAsync(config, deploymentPlan, sqlScript, riskAssessment, tagResult);
                }

                // 16. Display results
                DisplayResults(config, discoveredEntities, riskAssessment, deploymentPlan, tagResult, licenseSession);

                var exitCode = DetermineExitCode(riskAssessment);
                _logger.LogInformation("✅ SQL Schema Generator completed with exit code: {ExitCode}", exitCode);
                return exitCode;
            }
            catch (SqlSchemaException ex)
            {
                _logger.LogError("❌ SQL Schema Generator failed: {Message}", ex.Message);
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in SQL Schema Generator");
                return (int)SqlSchemaExitCode.InvalidConfiguration;
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

        private async Task<int> RunNoOpModeAsync(SqlSchemaConfiguration config)
        {
            try
            {
                _logger.LogWarning("⚠️ Running in NO-OP mode due to license unavailability");
                _logger.LogInformation("Analysis will be performed but no changes will be applied");

                // Perform analysis without license
                await ValidateAndConfigureGitAsync(config);
                var discoveredEntities = await _entityDiscoveryService.DiscoverEntitiesAsync(config);
                var currentSchema = await _schemaAnalysisService.AnalyzeCurrentSchemaAsync(config);
                var targetSchema = await _schemaAnalysisService.GenerateTargetSchemaAsync(discoveredEntities, config);
                var validationResult = await _schemaValidationService.ValidateSchemaChangesAsync(currentSchema, targetSchema, config);
                var riskAssessment = await _riskAssessmentService.AssessRiskAsync(validationResult, config);
                var deploymentPlan = await _deploymentPlanService.GenerateDeploymentPlanAsync(validationResult, riskAssessment, config);
                var sqlScript = await _sqlGenerationService.GenerateDeploymentScriptAsync(deploymentPlan, config);
                var tagResult = await _tagTemplateService.GenerateTagAsync(config, discoveredEntities);

                // Generate outputs but mark as NOOP
                await _fileOutputService.GenerateOutputsAsync(config, discoveredEntities, currentSchema, targetSchema,
                    validationResult, riskAssessment, deploymentPlan, sqlScript, tagResult, null);

                // Display results with NOOP warning
                DisplayNoOpResults(config, discoveredEntities, riskAssessment, deploymentPlan);

                var exitCode = DetermineExitCode(riskAssessment);
                _logger.LogWarning("✅ SQL Schema Generator completed in NO-OP mode with exit code: {ExitCode}", exitCode);
                return exitCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during NO-OP analysis");
                return (int)SqlSchemaExitCode.InvalidConfiguration;
            }
        }

        private async Task ValidateAndConfigureGitAsync(SqlSchemaConfiguration config)
        {
            var isValidRepo = await _gitOperationsService.IsValidGitRepositoryAsync();
            if (!isValidRepo)
            {
                throw new SqlSchemaException(SqlSchemaExitCode.GitOperationFailure,
                    "Not a valid git repository. Ensure the tool is running in a git repository.");
            }

            await _gitOperationsService.ConfigureGitAuthenticationAsync(config);
            _logger.LogInformation("✓ Git repository validated and configured");
        }

        private async Task ExecuteDeploymentAsync(SqlSchemaConfiguration config, DeploymentPlan deploymentPlan,
            SqlScript sqlScript, RiskAssessment riskAssessment, TagTemplateResult tagResult)
        {
            try
            {
                _logger.LogInformation("Executing database deployment...");

                // Create backup before deployment if enabled
                if (config.Backup.BackupBeforeDeployment && !config.Operation.SkipBackup)
                {
                    _logger.LogInformation("Creating database backup...");
                    await _backupService.CreateBackupAsync(config);
                }

                // Execute deployment
                var deploymentResult = await _deploymentExecutionService.ExecuteDeploymentAsync(deploymentPlan, sqlScript, config);

                if (deploymentResult.Success)
                {
                    // Create git tag after successful deployment
                    await _gitOperationsService.CreateTagAsync(tagResult.GeneratedTag,
                        $"Schema deployment v{tagResult.TokenValues.GetValueOrDefault("version", "1.0.0")}");

                    _logger.LogInformation("✓ Database deployment completed successfully");
                }
                else
                {
                    throw new SqlSchemaException(SqlSchemaExitCode.DeploymentExecutionFailure,
                        $"Deployment failed: {deploymentResult.ErrorMessage}");
                }
            }
            catch (SqlSchemaException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deployment execution failed");
                throw new SqlSchemaException(SqlSchemaExitCode.DeploymentExecutionFailure,
                    $"Deployment execution failed: {ex.Message}", ex);
            }
        }

        private int DetermineExitCode(RiskAssessment riskAssessment)
        {
            return riskAssessment.OverallRiskLevel switch
            {
                RiskLevel.Safe => (int)SqlSchemaExitCode.Success,
                RiskLevel.Warning => (int)SqlSchemaExitCode.WarningApprovalRequired,
                RiskLevel.Risky => (int)SqlSchemaExitCode.RiskyDualApprovalRequired,
                _ => (int)SqlSchemaExitCode.Success
            };
        }

        private void DisplayResults(SqlSchemaConfiguration config, EntityDiscoveryResult discoveredEntities,
            RiskAssessment riskAssessment, DeploymentPlan deploymentPlan, TagTemplateResult tagResult,
            LicenseSession licenseSession)
        {
            Console.WriteLine();
            Console.WriteLine("🗄️ SQL Schema Generator Results");
            Console.WriteLine();

            Console.WriteLine($"Language:           {config.Language.GetSelectedLanguage().ToUpperInvariant()}");
            Console.WriteLine($"Database Provider:  {config.Database.GetSelectedProvider().ToUpperInvariant()}");
            Console.WriteLine($"Track Attribute:    {config.TrackAttribute}");
            Console.WriteLine($"Entities Discovered: {discoveredEntities.Entities.Count}");
            Console.WriteLine($"Deployment Phases:  {deploymentPlan.Phases.Count}");
            Console.WriteLine($"Risk Level:         {riskAssessment.OverallRiskLevel.ToString().ToUpperInvariant()}");
            Console.WriteLine($"Generated Tag:      {tagResult.GeneratedTag}");
            Console.WriteLine();

            if (riskAssessment.OverallRiskLevel != RiskLevel.Safe)
            {
                Console.WriteLine($"⚠️ RISK ASSESSMENT: {riskAssessment.OverallRiskLevel.ToString().ToUpperInvariant()} OPERATIONS DETECTED ⚠️");
                if (riskAssessment.OverallRiskLevel == RiskLevel.Warning)
                {
                    Console.WriteLine("This deployment requires review by 1 approver before execution");
                }
                else if (riskAssessment.OverallRiskLevel == RiskLevel.Risky)
                {
                    Console.WriteLine("This deployment requires dual approval before execution");
                }

                Console.WriteLine("Risk Summary:");
                foreach (var risk in riskAssessment.RiskFactors.Take(3))
                {
                    Console.WriteLine($"  - {risk.Description} (Risk: {risk.RiskLevel})");
                }
                Console.WriteLine();
            }

            Console.WriteLine($"Repository:     {config.RepoUrl}");
            Console.WriteLine($"Branch:         {config.Branch}");
            Console.WriteLine($"Environment:    {config.Environment.Environment.ToUpperInvariant()}");
            Console.WriteLine($"Mode:           {config.Operation.Mode.ToUpperInvariant()}");
            Console.WriteLine($"Database:       {config.Database.Server}/{config.Database.DatabaseName}");

            if (licenseSession?.BurstMode == true)
            {
                Console.WriteLine();
                Console.WriteLine("⚠️ BURST MODE USAGE NOTICE ⚠️");
                Console.WriteLine($"This generation used burst capacity ({licenseSession.BurstCountRemaining} remaining this month)");
                Console.WriteLine("Consider purchasing additional licenses to avoid future interruptions");
            }
        }

        private void DisplayNoOpResults(SqlSchemaConfiguration config, EntityDiscoveryResult discoveredEntities,
            RiskAssessment riskAssessment, DeploymentPlan deploymentPlan)
        {
            Console.WriteLine();
            Console.WriteLine("🗄️ SQL Schema Generator Results (NO-OP MODE)");
            Console.WriteLine("   ⚠️ License unavailable - analysis only, no changes applied");
            Console.WriteLine();

            Console.WriteLine($"Language:           {config.Language.GetSelectedLanguage().ToUpperInvariant()}");
            Console.WriteLine($"Database Provider:  {config.Database.GetSelectedProvider().ToUpperInvariant()}");
            Console.WriteLine($"Track Attribute:    {config.TrackAttribute}");
            Console.WriteLine($"Entities Analyzed:  {discoveredEntities.Entities.Count}");
            Console.WriteLine($"Deployment Phases:  {deploymentPlan.Phases.Count}");
            Console.WriteLine($"Risk Level:         {riskAssessment.OverallRiskLevel.ToString().ToUpperInvariant()}");
            Console.WriteLine();

            Console.WriteLine("⚠️ NO CHANGES WERE APPLIED ⚠️");
            Console.WriteLine("License server was unavailable - pipeline analysis completed but");
            Console.WriteLine("no database changes were made and no git operations were performed.");
            Console.WriteLine();
        }
    }
}