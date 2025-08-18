using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for a service that generates the deployment.manifest.json from a code-based definition.
    /// </summary>
    public interface IManifestGeneratorService
    {
        Task<JsonDocument> GenerateAsync(string sourcePath);
    }

    /// <summary>
    /// Uses the Roslyn compiler API to scan C# source code for a `DeploymentDefinition` class
    /// and generates a JSON manifest from its DSL attributes.
    /// </summary>
    public class ManifestGeneratorService : IManifestGeneratorService
    {
        private readonly ILogger<ManifestGeneratorService> _logger;

        public ManifestGeneratorService(ILogger<ManifestGeneratorService> logger)
        {
            _logger = logger;
        }

        public async Task<JsonDocument> GenerateAsync(string sourcePath)
        {
            _logger.LogInformation("Starting manifest generation from source path: {Path}", sourcePath);

            if (!Directory.Exists(sourcePath))
            {
                throw new AssemblerException(AssemblerExitCode.ManifestGenerationFailure, $"Source path for manifest generation does not exist: {sourcePath}");
            }

            var definitionFiles = Directory.GetFiles(sourcePath, "*DeploymentDefinition.cs", SearchOption.AllDirectories);
            if (definitionFiles.Length == 0)
            {
                throw new AssemblerException(AssemblerExitCode.ManifestGenerationFailure, "No '*DeploymentDefinition.cs' file found in the provided source path.");
            }
            if (definitionFiles.Length > 1)
            {
                _logger.LogWarning("Multiple '*DeploymentDefinition.cs' files found. Using the first one: {File}", definitionFiles[0]);
            }

            var filePath = definitionFiles[0];
            var sourceCode = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = await syntaxTree.GetRootAsync();
            var classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.Text.EndsWith("DeploymentDefinition"));

            if (classDeclaration == null)
            {
                throw new AssemblerException(AssemblerExitCode.ManifestGenerationFailure, $"Could not find a class named 'DeploymentDefinition' in {filePath}.");
            }

            var manifest = new
            {
                groups = ParseNestedClass(classDeclaration, "Groups"),
                contracts = ParseNestedClass(classDeclaration, "Contracts"),
                connections = ParseConnectionsManifest() // Connections are in a separate, simpler manifest
            };

            _logger.LogInformation("✓ Manifest generation successful. Found {GroupCount} groups and {ContractCount} contracts.", manifest.groups.Count, manifest.contracts.Count);

            var jsonString = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return JsonDocument.Parse(jsonString);
        }

        private Dictionary<string, object> ParseNestedClass(ClassDeclarationSyntax parentClass, string nestedClassName)
        {
            var results = new Dictionary<string, object>();
            var nestedClass = parentClass.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.Text == nestedClassName);

            if (nestedClass == null)
            {
                _logger.LogDebug("No nested class named '{ClassName}' found in DeploymentDefinition.", nestedClassName);
                return results;
            }

            var properties = nestedClass.DescendantNodes().OfType<PropertyDeclarationSyntax>();
            foreach (var prop in properties)
            {
                var propName = prop.Identifier.Text;
                var attributes = prop.AttributeLists.SelectMany(al => al.Attributes);
                var attributeData = new Dictionary<string, object>();

                foreach (var attr in attributes)
                {
                    foreach (var arg in attr.ArgumentList?.Arguments ?? new SeparatedSyntaxList<AttributeArgumentSyntax>())
                    {
                        var argName = arg.NameEquals?.Name.Identifier.Text;
                        var argValue = arg.Expression.ToString().Trim('"');

                        // Handle enum values like `Cloud.Azure`
                        if (argValue.Contains("."))
                        {
                            argValue = argValue.Split('.').Last();
                        }

                        if (argName != null)
                        {
                            attributeData[argName.ToLowerInvariant()] = argValue;
                        }
                    }
                }
                results[propName] = attributeData;
            }
            return results;
        }

        private JsonElement ParseConnectionsManifest()
        {
            var connectionsManifestPath = Path.Combine(Directory.GetCurrentDirectory(), "connections.manifest.json");
            _logger.LogInformation("Looking for connections manifest at: {Path}", connectionsManifestPath);

            if (!File.Exists(connectionsManifestPath))
            {
                _logger.LogWarning("`connections.manifest.json` not found. No connection strings will be configured for generated APIs.");
                return JsonSerializer.SerializeToElement(new { connections = new { } });
            }

            try
            {
                var jsonContent = File.ReadAllText(connectionsManifestPath);
                using var jsonDoc = JsonDocument.Parse(jsonContent);
                // We only care about the 'connections' property, return it directly.
                if (jsonDoc.RootElement.TryGetProperty("connections", out var connectionsElement))
                {
                    return connectionsElement.Clone();
                }
                _logger.LogWarning("`connections.manifest.json` is present but does not contain a top-level 'connections' object.");
                return JsonSerializer.SerializeToElement(new { });
            }
            catch (JsonException ex)
            {
                throw new AssemblerException(AssemblerExitCode.ManifestGenerationFailure, $"Failed to parse `connections.manifest.json`: {ex.Message}");
            }
        }
    }
}