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
    /// Implements the ILanguageGenerator contract for the Java language using Spring Boot.
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
            var packageName = "com.x3sc.generated";
            var packagePath = $"src/main/java/{packageName.Replace('.', '/')}";
            var controllersPath = Path.Combine(projectPath, packagePath, "controllers");
            Directory.CreateDirectory(controllersPath);

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                var originalNamespace = api.GetProperty("namespace").GetString();
                if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(originalNamespace)) continue;

                var controllerName = $"{className}Controller";
                var filePath = Path.Combine(controllersPath, $"{controllerName}.java");

                try
                {
                    var sb = new StringBuilder();

                    sb.AppendLine($"package {packageName}.controllers;");
                    sb.AppendLine();
                    sb.AppendLine("import org.springframework.web.bind.annotation.RestController;");
                    sb.AppendLine("import org.springframework.web.bind.annotation.GetMapping;");
                    sb.AppendLine("import org.springframework.web.bind.annotation.PostMapping;");
                    sb.AppendLine("import org.springframework.web.bind.annotation.PutMapping;");
                    sb.AppendLine("import org.springframework.web.bind.annotation.DeleteMapping;");
                    sb.AppendLine("import org.springframework.web.bind.annotation.RequestMapping;");
                    sb.AppendLine("import org.springframework.web.bind.annotation.PathVariable;");
                    sb.AppendLine("import org.springframework.web.bind.annotation.RequestBody;");
                    sb.AppendLine();
                    sb.AppendLine($"// Assuming '{originalNamespace}' maps to a Java package structure for dependency.");
                    sb.AppendLine($"// You may need to manually add import for '{originalNamespace}.{className}' if it's not on classpath.");
                    sb.AppendLine($"// import {originalNamespace}.{className}; // Example import");
                    sb.AppendLine();
                    sb.AppendLine("@RestController");
                    sb.AppendLine($"@RequestMapping(\"{api.GetProperty("endpointAttributes")[0].GetProperty("arguments")[0].GetString()}\")"); // Base Path
                    sb.AppendLine($"public class {controllerName} {{");
                    sb.AppendLine();
                    sb.AppendLine($"    private final {className} {className.ToLowerInvariant()}Service;"); // Injected service
                    sb.AppendLine();
                    sb.AppendLine($"    public {controllerName}({className} {className.ToLowerInvariant()}Service) {{");
                    sb.AppendLine($"        this.{className.ToLowerInvariant()}Service = {className.ToLowerInvariant()}Service;");
                    sb.AppendLine("    }");
                    sb.AppendLine();

                    foreach (var method in api.GetProperty("methods").EnumerateArray())
                    {
                        var httpVerb = method.GetProperty("httpAttribute").GetProperty("type").GetString()?.ToLower() ?? "get";
                        var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "";
                        var methodName = method.GetProperty("methodName").GetString();
                        var returnType = method.GetProperty("returnType").GetString();
                        var parameters = method.GetProperty("parameters").EnumerateArray().ToList();

                        var paramSignature = string.Join(", ", parameters.Select(p =>
                        {
                            var type = p.GetProperty("type").GetString();
                            var name = p.GetProperty("name").GetString();
                            // Basic heuristic for path vs body. Can be more sophisticated.
                            if (route.Contains($"{{{name}}}")) return $"@PathVariable(\"{name}\") {type} {name}";
                            if (parameters.Count == 1 && httpVerb.Equals("post", StringComparison.OrdinalIgnoreCase)) return $"@RequestBody {type} {name}"; // Assume single POST param is body
                            return $"{type} {name}";
                        }));
                        var paramInvocation = string.Join(", ", parameters.Select(p => p.GetProperty("name").GetString()));

                        sb.AppendLine($"    @{char.ToUpper(httpVerb[0]) + httpVerb.Substring(1)}Mapping(\"{route}\")");
                        sb.AppendLine($"    public {returnType} {methodName}({paramSignature}) {{");
                        sb.AppendLine($"        return this.{className.ToLowerInvariant()}Service.{methodName}({paramInvocation});");
                        sb.AppendLine("    }");
                        sb.AppendLine();
                    }

                    sb.AppendLine("}");

                    await File.WriteAllTextAsync(filePath, sb.ToString());
                    generatedFiles.Add(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate source code for controller '{ControllerName}'.", controllerName);
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate Java source for '{controllerName}'.", ex);
                }
            }
            return generatedFiles;
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var filePath = Path.Combine(projectPath, "pom.xml");
            _logger.LogInformation("Generating Maven project file (pom.xml): {Path}", filePath);

            try
            {
                var inferredDependencies = await _dependencyInferenceService.InferDependenciesAsync(apisForGroup);
                var injectedDependencies = new Dictionary<string, string>();

                if (groupConfig.TryGetProperty("dependencies", out var deps) && deps.TryGetProperty("packages", out var packages))
                {
                    foreach (var prop in packages.EnumerateObject())
                    {
                        injectedDependencies[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }

                var springBootVersion = groupConfig.TryGetProperty("dependencies", out deps) && deps.TryGetProperty("framework", out var fw)
                    ? fw.GetString()
                    : "3.2.2"; // A sensible default

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
                        new XElement("artifactId", Path.GetFileName(projectPath)?.ToLowerInvariant() ?? "generated-api"),
                        new XElement("version", "1.0.0-SNAPSHOT"),
                        new XElement("name", Path.GetFileName(projectPath) ?? "generated-api-shim"), // Added name element
                        new XElement("description", "Generated API Shim by 3SC API Assembler"), // Added description
                        new XElement("properties",
                            new XElement("java.version", "17")
                        ),
                        new XElement("dependencies",
                            new XElement("dependency",
                                new XElement("groupId", "org.springframework.boot"),
                                new XElement("artifactId", "spring-boot-starter-web")
                            ),
                            // Injected dependencies from manifest would be added here
                            injectedDependencies.Select(dep => new XElement("dependency",
                                new XElement("groupId", dep.Key.Split(':')[0]), // Assuming "groupId:artifactId"
                                new XElement("artifactId", dep.Key.Split(':')[1]),
                                new XElement("version", dep.Value)
                            ))
                        )
                    )
                );

                var declaration = new XDeclaration("1.0", "UTF-8", null);
                var docWithDeclaration = new XDocument(declaration, pomXml.Root);

                await File.WriteAllTextAsync(filePath, docWithDeclaration.ToString());
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate pom.xml file at '{Path}'.", filePath);
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate Java project file '{filePath}'.", ex);
            }
        }
    }
}