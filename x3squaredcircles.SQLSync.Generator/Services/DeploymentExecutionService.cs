using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IDeploymentExecutionService
    {
        Task<DeploymentResult> ExecuteDeploymentAsync(DeploymentPlan deploymentPlan, SqlScript sqlScript, SqlSchemaConfiguration config);
    }

    public class DeploymentExecutionService : IDeploymentExecutionService
    {
        private readonly IDatabaseProviderFactory _databaseProviderFactory;
        private readonly ILogger<DeploymentExecutionService> _logger;

        public DeploymentExecutionService(IDatabaseProviderFactory databaseProviderFactory, ILogger<DeploymentExecutionService> logger)
        {
            _databaseProviderFactory = databaseProviderFactory;
            _logger = logger;
        }

        public async Task<DeploymentResult> ExecuteDeploymentAsync(DeploymentPlan deploymentPlan, SqlScript sqlScript, SqlSchemaConfiguration config)
        {
            var deploymentResult = new DeploymentResult
            {
                Success = false,
                PhaseResults = new List<PhaseResult>(),
                StartTime = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["database_provider"] = config.Database.GetSelectedProvider(),
                    ["database_name"] = config.Database.DatabaseName,
                    ["environment"] = config.Environment.Environment,
                    ["total_phases"] = deploymentPlan.Phases.Count,
                    ["total_operations"] = deploymentPlan.Phases.Sum(p => p.Operations.Count),
                    ["deployment_mode"] = config.Operation.Mode
                }
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Starting deployment execution for {PhaseCount} phases", deploymentPlan.Phases.Count);

                // Pre-deployment validation
                await ValidateDeploymentPrerequisitesAsync(config);

                // Get database provider
                var provider = await _databaseProviderFactory.GetProviderAsync(config.Database.GetSelectedProvider());

                // Test database connectivity
                if (!await provider.TestConnectionAsync(config))
                {
                    throw new InvalidOperationException("Unable to connect to target database");
                }

                // Execute phases in order
                var phaseNumber = 1;
                foreach (var phase in deploymentPlan.Phases.OrderBy(p => p.PhaseNumber))
                {
                    var phaseResult = await ExecutePhaseAsync(phase, provider, config, phaseNumber);
                    deploymentResult.PhaseResults.Add(phaseResult);

                    if (!phaseResult.Success)
                    {
                        deploymentResult.ErrorMessage = $"Phase {phaseNumber} ({phase.Name}) failed: {phaseResult.ErrorMessage}";
                        _logger.LogError("Deployment failed at phase {PhaseNumber}: {ErrorMessage}", phaseNumber, phaseResult.ErrorMessage);

                        // Attempt rollback if possible
                        await AttemptRollbackAsync(deploymentPlan, deploymentResult, provider, config, phaseNumber - 1);
                        break;
                    }

                    phaseNumber++;
                }

                // Check if all phases completed successfully
                deploymentResult.Success = deploymentResult.PhaseResults.All(pr => pr.Success);

                if (deploymentResult.Success)
                {
                    _logger.LogInformation("✓ Deployment completed successfully in {Duration}", stopwatch.Elapsed);

                    // Post-deployment validation
                    await ValidateDeploymentCompletionAsync(config, provider);
                }
                else
                {
                    _logger.LogError("❌ Deployment failed after {Duration}", stopwatch.Elapsed);
                }
            }
            catch (Exception ex)
            {
                deploymentResult.Success = false;
                deploymentResult.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Deployment execution failed with exception");
            }
            finally
            {
                stopwatch.Stop();
                deploymentResult.Duration = stopwatch.Elapsed;
                deploymentResult.EndTime = DateTime.UtcNow;

                // Log final deployment statistics
                LogDeploymentStatistics(deploymentResult);
            }

            return deploymentResult;
        }

        private async Task ValidateDeploymentPrerequisitesAsync(SqlSchemaConfiguration config)
        {
            _logger.LogDebug("Validating deployment prerequisites");

            // Validate configuration
            if (string.IsNullOrEmpty(config.Database.DatabaseName))
            {
                throw new InvalidOperationException("Target database name is required");
            }

            // Validate environment
            var environment = config.Environment.Environment.ToLowerInvariant();
            if (environment is "beta" or "prod")
            {
                if (string.IsNullOrEmpty(config.Environment.Vertical))
                {
                    throw new InvalidOperationException($"Vertical is required for {environment} deployments");
                }
            }

            // Validate operation mode
            if (config.Operation.ValidateOnly && config.Operation.Mode == "execute")
            {
                throw new InvalidOperationException("Cannot execute deployment in validate-only mode");
            }

            if (config.Operation.NoOp && config.Operation.Mode == "execute")
            {
                throw new InvalidOperationException("Cannot execute deployment in no-op mode");
            }
        }

        private async Task<PhaseResult> ExecutePhaseAsync(DeploymentPhase phase, IDatabaseProvider provider, SqlSchemaConfiguration config, int actualPhaseNumber)
        {
            var phaseResult = new PhaseResult
            {
                PhaseNumber = actualPhaseNumber,
                PhaseName = phase.Name,
                Success = false,
                Metadata = new Dictionary<string, object>
                {
                    ["phase_risk_level"] = phase.RiskLevel.ToString(),
                    ["requires_approval"] = phase.RequiresApproval,
                    ["can_rollback"] = phase.CanRollback,
                    ["operation_count"] = phase.Operations.Count
                }
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Executing Phase {PhaseNumber}: {PhaseName} ({OperationCount} operations)",
                    actualPhaseNumber, phase.Name, phase.Operations.Count);

                // Check if phase requires approval
                if (phase.RequiresApproval && !IsApprovalBypassAllowed(config))
                {
                    _logger.LogWarning("Phase {PhaseNumber} requires approval but approval bypass is not configured", actualPhaseNumber);
                    throw new InvalidOperationException($"Phase {actualPhaseNumber} ({phase.Name}) requires approval before execution");
                }

                // Skip empty phases
                if (!phase.Operations.Any())
                {
                    _logger.LogInformation("Phase {PhaseNumber} has no operations, skipping", actualPhaseNumber);
                    phaseResult.Success = true;
                    return phaseResult;
                }

                // Execute phase operations
                var operationsExecuted = 0;
                var useTransaction = ShouldUseTransactionForPhase(phase, config);

                if (useTransaction)
                {
                    operationsExecuted = await ExecutePhaseWithTransactionAsync(phase, provider, config);
                }
                else
                {
                    operationsExecuted = await ExecutePhaseWithoutTransactionAsync(phase, provider, config);
                }

                phaseResult.OperationsExecuted = operationsExecuted;
                phaseResult.Success = true;

                _logger.LogInformation("✓ Phase {PhaseNumber} completed: {OperationsExecuted}/{TotalOperations} operations executed",
                    actualPhaseNumber, operationsExecuted, phase.Operations.Count);
            }
            catch (Exception ex)
            {
                phaseResult.Success = false;
                phaseResult.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Phase {PhaseNumber} failed: {ErrorMessage}", actualPhaseNumber, ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                phaseResult.Duration = stopwatch.Elapsed;
            }

            return phaseResult;
        }

        private async Task<int> ExecutePhaseWithTransactionAsync(DeploymentPhase phase, IDatabaseProvider provider, SqlSchemaConfiguration config)
        {
            using var connection = await provider.CreateConnectionAsync(config);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            var operationsExecuted = 0;

            try
            {
                foreach (var operation in phase.Operations)
                {
                    await ExecuteOperationAsync(operation, connection, transaction, config);
                    operationsExecuted++;
                }

                transaction.Commit();
                _logger.LogDebug("Phase transaction committed successfully");
            }
            catch (Exception)
            {
                try
                {
                    transaction.Rollback();
                    _logger.LogWarning("Phase transaction rolled back due to error");
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Failed to rollback phase transaction");
                }
                throw;
            }

            return operationsExecuted;
        }

        private async Task<int> ExecutePhaseWithoutTransactionAsync(DeploymentPhase phase, IDatabaseProvider provider, SqlSchemaConfiguration config)
        {
            var operationsExecuted = 0;

            foreach (var operation in phase.Operations)
            {
                try
                {
                    await ExecuteOperationAsync(operation, provider, config);
                    operationsExecuted++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Operation failed: {OperationType} {ObjectName}", operation.Type, operation.ObjectName);
                    throw;
                }
            }

            return operationsExecuted;
        }

        private async Task ExecuteOperationAsync(DeploymentOperation operation, IDatabaseProvider provider, SqlSchemaConfiguration config)
        {
            using var connection = await provider.CreateConnectionAsync(config);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = operation.SqlCommand;
            command.CommandTimeout = config.Database.CommandTimeoutSeconds;

            await ExecuteOperationWithRetryAsync(command, operation, config);
        }

        private async Task ExecuteOperationAsync(DeploymentOperation operation, DbConnection connection, DbTransaction transaction, SqlSchemaConfiguration config)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = operation.SqlCommand;
            command.CommandTimeout = config.Database.CommandTimeoutSeconds;

            await ExecuteOperationWithRetryAsync(command, operation, config);
        }

        private async Task ExecuteOperationWithRetryAsync(DbCommand command, DeploymentOperation operation, SqlSchemaConfiguration config)
        {
            var maxRetries = operation.RiskLevel == RiskLevel.Safe ? config.Database.RetryAttempts : 1;
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogDebug("Executing operation: {OperationType} {ObjectName} (attempt {Attempt})",
                        operation.Type, operation.ObjectName, attempt);

                    var rowsAffected = await command.ExecuteNonQueryAsync();

                    _logger.LogDebug("Operation completed: {OperationType} {ObjectName}, {RowsAffected} rows affected",
                        operation.Type, operation.ObjectName, rowsAffected);

                    return; // Success
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    if (attempt < maxRetries && IsRetryableException(ex))
                    {
                        var delay = TimeSpan.FromSeconds(config.Database.RetryIntervalSeconds * attempt);
                        _logger.LogWarning("Operation failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}: {ErrorMessage}",
                            attempt, maxRetries, delay, ex.Message);

                        await Task.Delay(delay);
                    }
                    else
                    {
                        _logger.LogError("Operation failed permanently after {Attempts} attempts: {ErrorMessage}",
                            attempt, ex.Message);
                        break;
                    }
                }
            }

            throw new InvalidOperationException($"Operation {operation.Type} {operation.ObjectName} failed after {maxRetries} attempts", lastException);
        }

        private bool IsRetryableException(Exception ex)
        {
            // Common retryable exceptions across database providers
            var exceptionType = ex.GetType().Name.ToLowerInvariant();
            var message = ex.Message.ToLowerInvariant();

            // Network-related issues
            if (message.Contains("timeout") ||
                message.Contains("connection") ||
                message.Contains("network") ||
                message.Contains("deadlock"))
            {
                return true;
            }

            // Provider-specific retryable exceptions
            return exceptionType switch
            {
                "sqlexception" => IsRetryableSqlServerException(ex),
                "npgsqlexception" => IsRetryablePostgreSqlException(ex),
                "mysqlexception" => IsRetryableMySqlException(ex),
                _ => false
            };
        }

        private bool IsRetryableSqlServerException(Exception ex)
        {
            // SQL Server specific error codes that are retryable
            if (ex.GetType().GetProperty("Number")?.GetValue(ex) is int errorNumber)
            {
                return errorNumber switch
                {
                    1205 => true, // Deadlock
                    1222 => true, // Lock request timeout
                    8645 => true, // Memory pressure
                    8651 => true, // Low memory condition
                    _ => false
                };
            }
            return false;
        }

        private bool IsRetryablePostgreSqlException(Exception ex)
        {
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("deadlock") ||
                   message.Contains("lock") ||
                   message.Contains("timeout");
        }

        private bool IsRetryableMySqlException(Exception ex)
        {
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("deadlock") ||
                   message.Contains("lock") ||
                   message.Contains("timeout");
        }

        private bool ShouldUseTransactionForPhase(DeploymentPhase phase, SqlSchemaConfiguration config)
        {
            // Don't use transactions for these operation types
            var nonTransactionalOperations = new[] { "BACKUP", "VALIDATION" };
            if (phase.Operations.All(op => nonTransactionalOperations.Contains(op.Type)))
            {
                return false;
            }

            // Use transactions for phases with multiple operations
            if (phase.Operations.Count > 1)
            {
                return true;
            }

            // Use transactions for risky single operations
            if (phase.RiskLevel == RiskLevel.Risky)
            {
                return true;
            }

            return false;
        }

        private bool IsApprovalBypassAllowed(SqlSchemaConfiguration config)
        {
            // In dev environment, approval can be bypassed
            if (config.Environment.Environment.ToLowerInvariant() == "dev")
            {
                return true;
            }

            // If validate-only mode, approval is not required for execution
            if (config.Operation.ValidateOnly || config.Operation.NoOp)
            {
                return true;
            }

            // Check for approval bypass environment variable
            var bypassApproval = Environment.GetEnvironmentVariable("BYPASS_APPROVAL");
            return bool.TryParse(bypassApproval, out var bypass) && bypass;
        }

        private async Task AttemptRollbackAsync(DeploymentPlan deploymentPlan, DeploymentResult deploymentResult, IDatabaseProvider provider, SqlSchemaConfiguration config, int lastSuccessfulPhase)
        {
            try
            {
                _logger.LogInformation("Attempting rollback of successfully executed phases (1 to {LastPhase})", lastSuccessfulPhase);

                var successfulPhases = deploymentPlan.Phases
                    .Where(p => p.PhaseNumber <= lastSuccessfulPhase)
                    .OrderByDescending(p => p.PhaseNumber);

                var rollbackCount = 0;

                foreach (var phase in successfulPhases)
                {
                    if (!phase.CanRollback)
                    {
                        _logger.LogWarning("Phase {PhaseNumber} ({PhaseName}) cannot be rolled back automatically",
                            phase.PhaseNumber, phase.Name);
                        continue;
                    }

                    try
                    {
                        await RollbackPhaseAsync(phase, provider, config);
                        rollbackCount++;
                        _logger.LogInformation("✓ Rolled back Phase {PhaseNumber} ({PhaseName})",
                            phase.PhaseNumber, phase.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to rollback Phase {PhaseNumber} ({PhaseName}): {ErrorMessage}",
                            phase.PhaseNumber, phase.Name, ex.Message);
                        // Continue with other phases
                    }
                }

                if (rollbackCount > 0)
                {
                    _logger.LogInformation("Rollback completed: {RollbackCount} phases rolled back", rollbackCount);
                    deploymentResult.Metadata["rollback_phases_count"] = rollbackCount;
                }
                else
                {
                    _logger.LogWarning("No phases could be rolled back automatically");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback process failed");
                deploymentResult.Metadata["rollback_error"] = ex.Message;
            }
        }

        private async Task RollbackPhaseAsync(DeploymentPhase phase, IDatabaseProvider provider, SqlSchemaConfiguration config)
        {
            var rollbackOperations = phase.Operations
                .Where(op => !string.IsNullOrWhiteSpace(op.RollbackCommand))
                .Reverse() // Execute rollback in reverse order
                .ToList();

            if (!rollbackOperations.Any())
            {
                _logger.LogDebug("Phase {PhaseNumber} has no rollback operations", phase.PhaseNumber);
                return;
            }

            using var connection = await provider.CreateConnectionAsync(config);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var operation in rollbackOperations)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = operation.RollbackCommand;
                    command.CommandTimeout = config.Database.CommandTimeoutSeconds;

                    await command.ExecuteNonQueryAsync();
                }

                transaction.Commit();
            }
            catch (Exception)
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Failed to rollback rollback transaction for phase {PhaseNumber}", phase.PhaseNumber);
                }
                throw;
            }
        }

        private async Task ValidateDeploymentCompletionAsync(SqlSchemaConfiguration config, IDatabaseProvider provider)
        {
            try
            {
                _logger.LogDebug("Validating deployment completion");

                // Test basic connectivity
                if (!await provider.TestConnectionAsync(config))
                {
                    throw new InvalidOperationException("Database connectivity test failed after deployment");
                }

                // Execute provider-specific validation
                var validationSql = GetPostDeploymentValidationQuery(config.Database.GetSelectedProvider());
                if (!string.IsNullOrEmpty(validationSql))
                {
                    var result = await provider.ExecuteQueryAsync(validationSql, config);
                    _logger.LogDebug("Post-deployment validation query executed successfully, returned {RowCount} rows",
                        result.Rows.Count);
                }

                _logger.LogInformation("✓ Deployment completion validation passed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Post-deployment validation failed: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        private string GetPostDeploymentValidationQuery(string provider)
        {
            return provider switch
            {
                "sqlserver" => "SELECT COUNT(*) AS table_count FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';",
                "postgresql" => "SELECT COUNT(*) AS table_count FROM information_schema.tables WHERE table_type = 'BASE TABLE';",
                "mysql" => "SELECT COUNT(*) AS table_count FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';",
                "oracle" => "SELECT COUNT(*) AS table_count FROM user_tables;",
                "sqlite" => "SELECT COUNT(*) AS table_count FROM sqlite_master WHERE type='table';",
                _ => ""
            };
        }

        private void LogDeploymentStatistics(DeploymentResult deploymentResult)
        {
            var totalOperations = deploymentResult.PhaseResults.Sum(pr => pr.OperationsExecuted);
            var successfulPhases = deploymentResult.PhaseResults.Count(pr => pr.Success);

            _logger.LogInformation("Deployment Statistics:");
            _logger.LogInformation("  Total Duration: {Duration}", deploymentResult.Duration);
            _logger.LogInformation("  Phases Executed: {SuccessfulPhases}/{TotalPhases}",
                successfulPhases, deploymentResult.PhaseResults.Count);
            _logger.LogInformation("  Operations Executed: {TotalOperations}", totalOperations);
            _logger.LogInformation("  Overall Success: {Success}", deploymentResult.Success);

            if (!deploymentResult.Success)
            {
                _logger.LogError("  Error: {ErrorMessage}", deploymentResult.ErrorMessage);
            }

            // Log phase-by-phase timing
            foreach (var phaseResult in deploymentResult.PhaseResults)
            {
                var status = phaseResult.Success ? "✓" : "❌";
                _logger.LogInformation("  {Status} Phase {PhaseNumber} ({PhaseName}): {Duration}, {OperationsExecuted} operations",
                    status, phaseResult.PhaseNumber, phaseResult.PhaseName, phaseResult.Duration, phaseResult.OperationsExecuted);
            }
        }
    }
}