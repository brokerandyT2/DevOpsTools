using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Implements the ILanguageGenerator contract for the Java language.
    /// </summary>
    public class JavaGenerator : ILanguageGenerator
    {
        private readonly ILogger<JavaGenerator> _logger;
        private readonly IDependencyInferenceService _dependencyInferenceService;

        public JavaGenerator(ILogger<JavaGenerator> logger, IDependencyInferenceService dependencyInferenceService)
        {
            _logger = logger;
            _dependencyInferenceService = dependencyInferenceService;
        }

        public async Task<List<string>> GenerateSourceCodeAsync(List<JsonElement> apisForGroup, string projectPath)
        {
            var generatedFiles = new List<string>();
            // Standard Maven source directory structure
            var packagePath = "src/main/java/com/x3sc/generated";
            var controllersPath = Path.Combine(projectPath, packagePath, "controllers");
            Directory.CreateDirectory(controllersPath);

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                var controllerName = $"{className}Controller";
                var sb = new StringBuilder();

                sb.AppendLine("package com.x3sc.generated.controllers;");
                sb.AppendLine();
                sb.AppendLine("import org.springframework.web.bind.annotation.RestController;");
                sb.AppendLine("import org.springframework.web.bind.annotation.GetMapping;");
                // More imports would be added based on methods
                sb.AppendLine();
                sb.AppendLine("@RestController");
                sb.AppendLine($"public class {controllerName} {{");
                sb.AppendLine();

                foreach (var method in api.GetProperty("methods").EnumerateArray())
                {
                    var httpVerb = method.GetProperty("httpAttribute").GetProperty("type").GetString()?.ToLower() ?? "get";
                    var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "";
                    var methodName = method.GetProperty("methodName").GetString();

                    sb.AppendLine($"    @GetMapping(\"{route}\")");
                    sb.AppendLine($"    public String {methodName}() {{");
                    sb.AppendLine($"        // Default generated shim for {methodName}.");
                    sb.AppendLine($"        return \"Response from {methodName}\";");
                    sb.AppendLine("    }");
                }

                sb.AppendLine("}");

                var filePath = Path.Combine(controllersPath, $"{controllerName}.java");
                await File.WriteAllTextAsync(filePath, sb.ToString());
                generatedFiles.Add(filePath);
            }
            return generatedFiles;
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var filePath = Path.Combine(projectPath, "pom.xml");
            _logger.LogInformation("Generating Maven project file: {Path}", filePath);

            var inferredDependencies = await _dependencyInferenceService.InferDependenciesAsync(apisForGroup);
            var injectedDependencies = new Dictionary<string, string>();

            if (groupConfig.TryGetProperty("dependencies", out var deps) && deps.TryGetProperty("packages", out var packages))
            {
                foreach (var prop in packages.EnumerateObject())
                {
                    injectedDependencies[prop.Name] = prop.Value.GetString();
                }
            }

            // A real implementation would need to map package names to groupId/artifactId
            var springBootVersion = groupConfig.GetProperty("dependencies").GetProperty("framework").GetString() ?? "3.2.0";

            var pomXml = new XDocument(
                new XElement("project",
                    new XAttribute("xmlns", "http://maven.apache.org/POM/4.0.0"),
                    new XElement("modelVersion", "4.0.0"),
                    new XElement("parent",
                        new XElement("groupId", "org.springframework.boot"),
                        new XElement("artifactId", "spring-boot-starter-parent"),
                        new XElement("version", springBootVersion)
                    ),
                    new XElement("groupId", "com.x3sc.generated"),
                    new XElement("artifactId", Path.GetFileName(projectPath)),
                    new XElement("version", "1.0.0-SNAPSHOT"),
                    new XElement("properties",
                        new XElement("java.version", "17")
                    ),
                    new XElement("dependencies",
                        new XElement("dependency",
                            new XElement("groupId", "org.springframework.boot"),
                            new XElement("artifactId", "spring-boot-starter-web")
                        )
                    // Inferred and injected dependencies would be added here
                    )
                )
            );

            await File.WriteAllTextAsync(filePath, pomXml.ToString());
            return filePath;
        }
    }
}