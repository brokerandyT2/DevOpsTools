using Microsoft.Extensions.Logging;
using Scriban;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
                var namespaceName = api.GetProperty("namespace").GetString();
                var controllerName = $"{className}Controller";
                var sb = new StringBuilder();

                sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
                sb.AppendLine("using System;");
                sb.AppendLine("using System.Threading.Tasks;");
                sb.AppendLine($"using {namespaceName};");
                sb.AppendLine();
                sb.AppendLine($"namespace {Path.GetFileName(projectPath)}.Controllers");
                sb.AppendLine("{");
                sb.AppendLine("    [ApiController]");
                sb.AppendLine($"    public partial class {controllerName} : ControllerBase");
                sb.AppendLine("    {");

                foreach (var method in api.GetProperty("methods").EnumerateArray())
                {
                    var templatePaths = method.GetProperty("useTemplatePaths").EnumerateArray().Select(e => e.GetString()).ToList();

                    if (templatePaths.Any())
                    {
                        sb.AppendLine(await RenderComposedTemplateAsync(method, api));
                    }
                    else
                    {
                        sb.AppendLine(RenderDefaultMethod(method));
                    }
                }

                sb.AppendLine("    }");
                sb.AppendLine("}");

                var filePath = Path.Combine(controllersPath, $"{controllerName}.cs");
                await File.WriteAllTextAsync(filePath, sb.ToString());
                generatedFiles.Add(filePath);
            }
            return generatedFiles;
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var projectName = Path.GetFileName(projectPath);
            var filePath = Path.Combine(projectPath, $"{projectName}.csproj");

            _logger.LogInformation("Generating C# project file: {Path}", filePath);

            var inferredDependencies = await _dependencyInferenceService.InferDependenciesAsync(apisForGroup);
            var injectedDependencies = new Dictionary<string, string>();

            if (groupConfig.TryGetProperty("dependencies", out var deps) && deps.TryGetProperty("packages", out var packages))
            {
                foreach (var prop in packages.EnumerateObject())
                {
                    injectedDependencies[prop.Name] = prop.Value.GetString();
                }
            }

            var allDependencies = inferredDependencies.Concat(injectedDependencies)
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.First().Value);

            var projectXml = new XDocument(
                new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk.Web"),
                    new XElement("PropertyGroup",
                        new XElement("TargetFramework", groupConfig.GetProperty("dependencies").GetProperty("framework").GetString() ?? "net8.0"),
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
                            .Distinct()
                            .Select(projPath => new XElement("ProjectReference",
                                new XAttribute("Include", projPath)
                            ))
                    )
                )
            );

            await File.WriteAllTextAsync(filePath, projectXml.ToString());
            return filePath;
        }

        private string RenderDefaultMethod(JsonElement method)
        {
            var sb = new StringBuilder();
            var httpVerb = method.GetProperty("httpAttribute").GetProperty("type").GetString() ?? "HttpGet";
            var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "";
            var methodName = method.GetProperty("methodName").GetString();
            var returnType = method.GetProperty("returnType").GetString();

            // This is a simplified rendering. A real implementation would parse parameters and construct a real call.
            sb.AppendLine($"        [{char.ToUpper(httpVerb[0]) + httpVerb.Substring(1)}Attribute(\"{route}\")]");
            sb.AppendLine($"        public {returnType} {methodName}()");
            sb.AppendLine("        {");
            sb.AppendLine($"            // Default generated shim for {methodName}.");
            sb.AppendLine($"            // A real implementation would call the underlying business logic service.");
            sb.AppendLine($"            throw new NotImplementedException();");
            sb.AppendLine("        }");
            return sb.ToString();
        }

        private async Task<string> RenderComposedTemplateAsync(JsonElement method, JsonElement apiClass)
        {
            var sb = new StringBuilder();
            var templatePaths = method.GetProperty("useTemplatePaths").EnumerateArray().Select(p => p.GetString()).ToList();
            _logger.LogInformation("Method '{MethodName}' uses composed templates. Rendering {Count} templates.", method.GetProperty("methodName").GetString(), templatePaths.Count);

            var templateContext = new
            {
                method = JsonDocument.Parse(method.ToString()).RootElement,
                api = JsonDocument.Parse(apiClass.ToString()).RootElement
            };

            foreach (var path in templatePaths)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Template file specified in [UseTemplate] attribute not found: {path}");
                }

                var templateContent = await File.ReadAllTextAsync(path);
                var template = Template.Parse(templateContent);

                if (template.HasErrors)
                {
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Scriban template parsing failed for '{path}': {string.Join(", ", template.Messages)}");
                }

                var result = await template.RenderAsync(templateContext, member => member.Name);
                sb.AppendLine(result);
            }
            return sb.ToString();
        }
    }
}