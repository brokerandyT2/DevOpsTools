using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Implements the ILanguageGenerator contract for the C# language.
    /// </summary>
    public class CSharpGenerator : ILanguageGenerator
    {
        private readonly ILogger<CSharpGenerator> _logger;
        private readonly IDependencyInferenceService _dependencyInferenceService;

        public CSharpGenerator(ILogger<CSharpGenerator> logger, IDependencyInferenceService dependencyInferenceService)
        {
            _logger = logger;
            _dependencyInferenceService = dependencyInferenceService;
        }

        public async Task<List<string>> GenerateSourceCodeAsync(List<JsonElement> apisForGroup, string projectPath)
        {
            var generatedFiles = new List<string>();
            var controllersPath = Path.Combine(projectPath, "Controllers");
            Directory.CreateDirectory(controllersPath);

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                if (string.IsNullOrEmpty(className)) continue;

                var controllerName = $"{className}Controller";
                var filePath = Path.Combine(controllersPath, $"{controllerName}.cs");

                try
                {
                    var sourceCode = await BuildControllerSourceAsync(api, projectPath);
                    await File.WriteAllTextAsync(filePath, sourceCode);
                    generatedFiles.Add(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate source code for controller '{ControllerName}'.", controllerName);
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate C# source for '{controllerName}'.", ex);
                }
            }
            return generatedFiles;
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var projectName = Path.GetFileName(projectPath);
            var filePath = Path.Combine(projectPath, $"{projectName}.csproj");
            _logger.LogInformation("Generating C# project file: {Path}", filePath);

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

                // Injected dependencies take precedence over inferred ones.
                var allDependencies = inferredDependencies
                    .Concat(injectedDependencies)
                    .GroupBy(kv => kv.Key)
                    .ToDictionary(g => g.Key, g => g.Last().Value); // Last() ensures injected wins

                var framework = groupConfig.TryGetProperty("dependencies", out deps) && deps.TryGetProperty("framework", out var fw)
                    ? fw.GetString()
                    : "net8.0";

                var projectXml = new XDocument(
                    new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk.Web"),
                        new XElement("PropertyGroup",
                            new XElement("TargetFramework", framework),
                            new XElement("ImplicitUsings", "enable"),
                            new XElement("Nullable", "enable")
                        ),
                        new XElement("ItemGroup",
                            allDependencies.Select(dep => new XElement("PackageReference",
                                    new XAttribute("Include", dep.Key),
                                    new XAttribute("Version", dep.Value)
                                ))
                        ),
                        new XElement("ItemGroup",
                            apisForGroup.Select(api => api.GetProperty("projectPath").GetString())
                                .Where(p => !string.IsNullOrEmpty(p))
                                .Distinct()
                                .Select(projPath => new XElement("ProjectReference",
                                    new XAttribute("Include", projPath)
                                ))
                        )
                    )
                );

                await File.WriteAllTextAsync(filePath, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + projectXml.ToString());
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate .csproj file at '{Path}'.", filePath);
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate C# project file '{filePath}'.", ex);
            }
        }

        private async Task<string> BuildControllerSourceAsync(JsonElement api, string projectPath)
        {
            var className = api.GetProperty("className").GetString()!;
            var namespaceName = api.GetProperty("namespace").GetString();
            var controllerName = $"{className}Controller";
            var sb = new StringBuilder();

            sb.AppendLine($"    public partial class {controllerName} : ControllerBase");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly {className} __{className.ToLowerInvariant()}Service;");
            sb.AppendLine();
            sb.AppendLine($"        public {controllerName}({className} {className.ToLowerInvariant()}Service)");
            sb.AppendLine("        {");
            sb.AppendLine($"            __{className.ToLowerInvariant()}Service = {className.ToLowerInvariant()}Service;");
            sb.AppendLine("        }");
            sb.AppendLine();

            foreach (var method in api.GetProperty("methods").EnumerateArray())
            {
                var templatePaths = method.TryGetProperty("useTemplatePaths", out var paths)
                    ? paths.EnumerateArray().Select(e => e.GetString()).ToList()
                    : new List<string?>();

                if (templatePaths.Any(p => !string.IsNullOrEmpty(p)))
                {
                    sb.AppendLine(await RenderComposedTemplateAsync(method, api, templatePaths!));
                }
                else
                {
                    sb.AppendLine(RenderDefaultMethod(method, api));
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string RenderDefaultMethod(JsonElement method, JsonElement apiClass)
        {
            var className = apiClass.GetProperty("className").GetString()!;
            var httpVerb = method.GetProperty("httpAttribute").GetProperty("type").GetString() ?? "HttpGet";
            var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "";
            var methodName = method.GetProperty("methodName").GetString()!;
            var returnType = method.GetProperty("returnType").GetString();
            var parameters = method.GetProperty("parameters").EnumerateArray()
                .Select(p => $"{p.GetProperty("type").GetString()} {p.GetProperty("name").GetString()}")
                .ToList();
            var parameterNames = method.GetProperty("parameters").EnumerateArray()
                .Select(p => p.GetProperty("name").GetString())
                .ToList();

            var parameterSignature = string.Join(", ", parameters);
            var parameterInvocation = string.Join(", ", parameterNames);

            var sb = new StringBuilder();
            sb.AppendLine($"        // Auto-generated shim method for {className}.{methodName}");
            sb.AppendLine($"        [{httpVerb}Attribute(\"{route}\")]");
            sb.AppendLine($"        public {returnType} {methodName}({parameterSignature})");
            sb.AppendLine("        {");
            sb.AppendLine($"            // This call is forwarded to the injected business logic service.");
            sb.AppendLine($"            return __{className.ToLowerInvariant()}Service.{methodName}({parameterInvocation});");
            sb.AppendLine("        }");
            return sb.ToString();
        }

        private async Task<string> RenderComposedTemplateAsync(JsonElement method, JsonElement apiClass, List<string> templatePaths)
        {
            var methodName = method.GetProperty("methodName").GetString();
            _logger.LogInformation("Method '{MethodName}' uses composed templates. Rendering {Count} template(s).", methodName, templatePaths.Count);

            var sb = new StringBuilder();

            // Create a disposable context for parsing
            using var methodDoc = JsonDocument.Parse(method.ToString());
            using var apiDoc = JsonDocument.Parse(apiClass.ToString());

            var templateContext = new
            {
                method = methodDoc.RootElement,
                api = apiDoc.RootElement
            };

            foreach (var path in templatePaths)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Template file specified for method '{methodName}' not found: {path}");
                }

                var templateContent = await File.ReadAllTextAsync(path);
                var template = Template.Parse(templateContent);

                if (template.HasErrors)
                {
                    var errors = string.Join(", ", template.Messages.Select(m => m.Message));
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Scriban template parsing failed for '{path}': {errors}");
                }

                var result = await template.RenderAsync(templateContext, member => member.Name);
                sb.AppendLine(result);
            }
            return sb.ToString();
        }
    }
}