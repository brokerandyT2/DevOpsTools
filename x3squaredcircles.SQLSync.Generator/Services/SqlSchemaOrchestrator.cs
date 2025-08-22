using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface ISqlSchemaOrchestrator
    {
        Task<int> RunAsync();
    }

    public class SqlSchemaOrchestrator : ISqlSchemaOrchestrator
    {
        private readonly SqlSchemaConfiguration _config;
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
        private readonly IControlPointService _controlPointService;
        private readonly ILogger<SqlSchemaOrchestrator> _logger;

        public SqlSchemaOrchestrator(
            SqlSchemaConfiguration config,
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
            IControlPointService controlPointService,
            ILogger<SqlSchemaOrchestrator> logger)
        {
            _config = config;
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
            _controlPointService = controlPointService;
            _logger = logger;
        }

        public async Task<int> RunAsync()
        {
            DeploymentResult deploymentResult = null;
            try
            {
                var mutableConfig = await _controlPointService.InterceptAsync(ControlPointStage.OnRunStart, _config);

                ValidateConfiguration(mutableConfig);
                LogConfigurationSummary(mutableConfig);

                if (mutableConfig.Vault.Type != VaultType.None)
                {
                    await _keyVaultService.ResolveSecretsAsync(mutableConfig);
                }

                _logger.LogInformation("Step 1/8: Discovering entities...");
                var discoveredEntities = await _entityDiscoveryService.DiscoverEntitiesAsync(mutableConfig);
                discoveredEntities = await _controlPointService.InterceptAsync(ControlPointStage.AfterDiscovery, discoveredEntities);

                _logger.LogInformation("Step 2/8: Analyzing current database schema...");
                var currentSchema = await _schemaAnalysisService.AnalyzeCurrentSchemaAsync(mutableConfig);

                _logger.LogInformation("Step 3/8: Generating target schema...");
                var targetSchema = await _schemaAnalysisService.GenerateTargetSchemaAsync(discoveredEntities, mutableConfig);

                _logger.LogInformation("Step 4/8: Validating schema changes...");
                var validationResult = await _schemaValidationService.ValidateSchemaChangesAsync(currentSchema, targetSchema, mutableConfig);
                validationResult = await _controlPointService.InterceptAsync(ControlPointStage.AfterValidation, validationResult);

                _logger.LogInformation("Step 5/8: Assessing deployment risk...");
                var riskAssessment = await _riskAssessmentService.AssessRiskAsync(validationResult, mutableConfig);
                riskAssessment = await _controlPointService.InterceptAsync(ControlPointStage.AfterRiskAssessment, riskAssessment);

                _logger.LogInformation("Step 6/8: Generating deployment plan...");
                var deploymentPlan = await _deploymentPlanService.GenerateDeploymentPlanAsync(validationResult, riskAssessment, mutableConfig);

                _logger.LogInformation("Step 7/8: Generating SQL deployment script...");
                var sqlScript = await _sqlGenerationService.GenerateDeploymentScriptAsync(deploymentPlan, mutableConfig);

                if (mutableConfig.Operation.Mode == OperationMode.Deploy && !mutableConfig.Operation.NoOp)
                {
                    _logger.LogInformation("Step 8/8: Executing deployment...");
                    deploymentPlan = await _controlPointService.InterceptAsync(ControlPointStage.BeforeBackup, deploymentPlan);
                    if (!mutableConfig.Backup.SkipBackup)
                    {
                        await _backupService.CreateBackupAsync(mutableConfig);
                    }

                    var executionPayload = new { DeploymentPlan = deploymentPlan, SqlScript = sqlScript };
                    await _controlPointService.InterceptAsync(ControlPointStage.BeforeExecution, executionPayload);

                    deploymentResult = await _deploymentExecutionService.ExecuteDeploymentAsync(deploymentPlan, sqlScript, mutableConfig);
                    if (!deploymentResult.Success)
                    {
                        throw new SqlSchemaException(SqlSchemaExitCode.DeploymentExecutionFailure, deploymentResult.ErrorMessage);
                    }
                }

                await _controlPointService.NotifyAsync(ControlPointStage.Completion, ControlPointEvent.OnSuccess, deploymentPlan);
                return (int)SqlSchemaExitCode.Success;
            }
            catch (Exception ex)
            {
                var exitCode = ex is SqlSchemaException schemaEx ? schemaEx.ExitCode : SqlSchemaExitCode.UnhandledException;
                _logger.LogError(ex, "Orchestration failed with exit code {ExitCode}: {Message}", exitCode, ex.Message);
                await _controlPointService.NotifyAsync(ControlPointStage.Completion, ControlPointEvent.OnFailure, new { ErrorMessage = ex.Message, ExitCode = exitCode });
                return (int)exitCode;
            }
        }

        private void ValidateConfiguration(SqlSchemaConfiguration config)
        {
            var errors = new List<string>();
            var langCount = new[] { config.Language.CSharp, config.Language.Java, config.Language.Go, config.Language.Python, config.Language.TypeScript }.Count(l => l);
            if (langCount != 1) errors.Add("Exactly one SQLSYNC_LANGUAGE_* variable must be set to 'true'.");
            if (string.IsNullOrWhiteSpace(config.TrackAttribute)) errors.Add("SQLSYNC_TRACK_ATTRIBUTE must be set.");
            if (string.IsNullOrWhiteSpace(config.Database.Server)) errors.Add("SQLSYNC_DB_SERVER must be set.");
            if (string.IsNullOrWhiteSpace(config.Database.DatabaseName)) errors.Add("SQLSYNC_DB_NAME must be set.");
            if (config.Authentication.AuthMode == AuthMode.Password && string.IsNullOrWhiteSpace(config.Authentication.Username)) errors.Add("SQLSYNC_DB_USERNAME must be set for Password authentication.");

            if (errors.Any())
            {
                throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration, $"Configuration validation failed: {string.Join(", ", errors)}");
            }
        }

        private void LogConfigurationSummary(SqlSchemaConfiguration config)
        {
            if (config.Logging.LogLevel > LogLevel.Information && !config.Logging.Verbose) return;

            _logger.LogInformation("--- Configuration Summary ---");
            _logger.LogInformation("Mode: {Mode}, No-Op: {NoOp}", config.Operation.Mode, config.Operation.NoOp);
            _logger.LogInformation("Database: {Provider} on {Server}/{Database}", config.Database.Provider, config.Database.Server, config.Database.DatabaseName);
            _logger.LogInformation("Authentication: {AuthMode}", config.Authentication.AuthMode);
            _logger.LogInformation("-----------------------------");
        }
    }
}