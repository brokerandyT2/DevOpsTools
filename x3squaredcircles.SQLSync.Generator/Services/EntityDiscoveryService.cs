using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IEntityDiscoveryService
    {
        Task<EntityDiscoveryResult> DiscoverEntitiesAsync(SqlSchemaConfiguration config);
    }

    public class EntityDiscoveryService : IEntityDiscoveryService
    {
        private readonly ILanguageAnalyzerFactory _languageAnalyzerFactory;
        private readonly ILogger<EntityDiscoveryService> _logger;
        private readonly string _workingDirectory = "/src";

        public EntityDiscoveryService(
            ILanguageAnalyzerFactory languageAnalyzerFactory,
            ILogger<EntityDiscoveryService> logger)
        {
            _languageAnalyzerFactory = languageAnalyzerFactory;
            _logger = logger;
        }

        public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(SqlSchemaConfiguration config)
        {
            var language = GetSelectedLanguageName(config);
            _logger.LogInformation("Starting entity discovery for language: {Language}, attribute: {TrackAttribute}", language, config.TrackAttribute);

            var analyzer = await _languageAnalyzerFactory.GetAnalyzerAsync(language);
            var sourcePath = GetSourcePathForLanguage(config, language);

            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            {
                throw new SqlSchemaException(SqlSchemaExitCode.EntityDiscoveryFailure, $"Source path for {language} is not configured or does not exist: '{sourcePath}'");
            }

            var discoveredEntities = await analyzer.DiscoverEntitiesAsync(sourcePath, config.TrackAttribute);

            var processedEntities = PostProcessEntities(discoveredEntities, config);

            if (!processedEntities.Any())
            {
                _logger.LogWarning("No entities found with attribute '{TrackAttribute}'. Ensure entities are marked and source/assembly paths are correct.", config.TrackAttribute);
            }

            _logger.LogInformation("✓ Entity discovery completed: {EntityCount} entities discovered.", processedEntities.Count);
            return new EntityDiscoveryResult { Entities = processedEntities };
        }

        private List<DiscoveredEntity> PostProcessEntities(List<DiscoveredEntity> entities, SqlSchemaConfiguration config)
        {
            foreach (var entity in entities)
            {
                // If TableName was not set by a DSL attribute, generate it from the class name.
                if (string.IsNullOrWhiteSpace(entity.TableName))
                {
                    entity.TableName = ToSnakeCase(entity.Name);
                    _logger.LogDebug("Generated TableName '{TableName}' for entity '{EntityName}'.", entity.TableName, entity.Name);
                }

                // If SchemaName was not set by a DSL attribute, use the default from config.
                if (string.IsNullOrWhiteSpace(entity.SchemaName))
                {
                    entity.SchemaName = config.Database.Schema;
                }
            }
            return entities;
        }

        private string GetSourcePathForLanguage(SqlSchemaConfiguration config, string language)
        {
            string path = language switch
            {
                "CSharp" => config.SchemaAnalysis.AssemblyPath,
                _ => config.SchemaAnalysis.SourcePaths,
            };

            if (string.IsNullOrWhiteSpace(path)) return null;

            return Path.IsPathRooted(path) ? path : Path.Combine(_workingDirectory, path);
        }

        private string GetSelectedLanguageName(SqlSchemaConfiguration config)
        {
            if (config.Language.CSharp) return "CSharp";
            if (config.Language.Java) return "Java";
            if (config.Language.Python) return "Python";
            if (config.Language.Go) return "Go";
            if (config.Language.TypeScript) return "TypeScript";
            return "Unknown";
        }

        private string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return Regex.Replace(input, @"(?<=[a-z0-9])(?=[A-Z])", "_").ToLowerInvariant();
        }
    }
}