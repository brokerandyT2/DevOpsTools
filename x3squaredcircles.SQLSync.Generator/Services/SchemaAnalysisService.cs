using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface ISchemaAnalysisService
    {
        Task<DatabaseSchema> AnalyzeCurrentSchemaAsync(SqlSchemaConfiguration config);
        Task<DatabaseSchema> GenerateTargetSchemaAsync(EntityDiscoveryResult entities, SqlSchemaConfiguration config);
    }

    public class SchemaAnalysisService : ISchemaAnalysisService
    {
        private readonly IDatabaseProviderFactory _databaseProviderFactory;
        private readonly ILogger<SchemaAnalysisService> _logger;

        public SchemaAnalysisService(
            IDatabaseProviderFactory databaseProviderFactory,
            ILogger<SchemaAnalysisService> logger)
        {
            _databaseProviderFactory = databaseProviderFactory;
            _logger = logger;
        }

        public async Task<DatabaseSchema> AnalyzeCurrentSchemaAsync(SqlSchemaConfiguration config)
        {
            _logger.LogInformation("Analyzing current database schema for {Provider} at {Server}/{DatabaseName}",
                config.Database.Provider, config.Database.Server, config.Database.DatabaseName);

            try
            {
                var provider = await _databaseProviderFactory.GetProviderAsync(config.Database.Provider.ToString());
                var currentSchema = await provider.GetCurrentSchemaAsync(config);

                _logger.LogInformation("✓ Current schema analysis complete: Found {TableCount} tables.", currentSchema.Tables.Count);
                return currentSchema;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze current database schema.");
                throw new SqlSchemaException(SqlSchemaExitCode.SchemaAnalysisFailure, "Failed to analyze current database schema.", ex);
            }
        }

        public async Task<DatabaseSchema> GenerateTargetSchemaAsync(EntityDiscoveryResult entities, SqlSchemaConfiguration config)
        {
            _logger.LogInformation("Generating target schema from {EntityCount} discovered entities...", entities.Entities.Count);

            var targetSchema = new DatabaseSchema();

            foreach (var entity in entities.Entities)
            {
                var table = new SchemaTable
                {
                    Name = entity.TableName,
                    Schema = entity.SchemaName,
                    Columns = entity.Properties.Select(prop => new SchemaColumn
                    {
                        Name = prop.Name,
                        DataType = prop.SqlType,
                        IsNullable = prop.IsNullable
                    }).ToList()
                };
                targetSchema.Tables.Add(table);
            }

            _logger.LogInformation("✓ Target schema generation complete: {TableCount} tables defined.", targetSchema.Tables.Count);
            await Task.CompletedTask;
            return targetSchema;
        }
    }
}