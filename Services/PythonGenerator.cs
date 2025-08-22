using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Implements the ILanguageGenerator contract for the Python language using FastAPI.
    /// </summary>
    public class PythonGenerator : ILanguageGenerator
    {
        private readonly ILogger<PythonGenerator> _logger;
        public PythonGenerator(ILogger<PythonGenerator> logger)
        {
            _logger = logger;
        }

        public async Task<List<string>> GenerateSourceCodeAsync(List<JsonElement> apisForGroup, string projectPath)
        {
            var generatedFiles = new List<string>();
            var routersPath = Path.Combine(projectPath, "routers");
            var servicesPath = Path.Combine(projectPath, "services");
            Directory.CreateDirectory(routersPath);
            Directory.CreateDirectory(servicesPath);

            // Create __init__.py files to make directories into Python packages
            await File.WriteAllTextAsync(Path.Combine(routersPath, "__init__.py"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(servicesPath, "__init__.py"), string.Empty);
            generatedFiles.Add(Path.Combine(routersPath, "__init__.py"));
            generatedFiles.Add(Path.Combine(servicesPath, "__init__.py"));

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                if (string.IsNullOrEmpty(className)) continue;

                // 1. Generate the service stub (placeholder for real business logic)
                var serviceFilePath = Path.Combine(servicesPath, $"{className.ToLowerInvariant()}_service.py");
                await GenerateServiceStub(className, api, serviceFilePath);
                generatedFiles.Add(serviceFilePath);

                // 2. Generate the router that depends on the service
                var routerFileName = $"{className.ToLowerInvariant()}_router.py";
                var routerFilePath = Path.Combine(routersPath, routerFileName);

                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("from fastapi import APIRouter, Depends");
                    sb.AppendLine($"from services.{className.ToLowerInvariant()}_service import {className}"); // Import the service
                    sb.AppendLine();
                    sb.AppendLine("router = APIRouter()");
                    sb.AppendLine();
                    sb.AppendLine($"# --- Routes for {className} ---");
                    sb.AppendLine();

                    foreach (var method in api.GetProperty("methods").EnumerateArray())
                    {
                        var httpVerb = method.GetProperty("httpAttribute").GetProperty("type").GetString()?.ToLowerInvariant() ?? "get";
                        var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "/";
                        var methodName = method.GetProperty("methodName").GetString();
                        var parameters = method.GetProperty("parameters").EnumerateArray().ToList();

                        var paramSignature = string.Join(", ", parameters.Select(p =>
                        {
                            var name = p.GetProperty("name").GetString();
                            var type = p.GetProperty("type").GetString();
                            // Basic Python type hints
                            var pyType = type switch { "int" => "int", "string" => "str", "bool" => "bool", _ => "any" };
                            return $"{name}: {pyType}";
                        }));
                        var paramInvocation = string.Join(", ", parameters.Select(p => p.GetProperty("name").GetString()));

                        // Add the dependency injection for the service
                        if (!string.IsNullOrEmpty(paramSignature)) paramSignature += ", ";
                        paramSignature += $"service: {className} = Depends({className})";

                        sb.AppendLine($"@router.{httpVerb}('{route}')");
                        sb.AppendLine($"async def {methodName}({paramSignature}):");
                        sb.AppendLine($"    return await service.{methodName}({paramInvocation})");
                        sb.AppendLine();
                    }

                    await File.WriteAllTextAsync(routerFilePath, sb.ToString());
                    generatedFiles.Add(routerFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate source code for router '{RouterName}'.", routerFileName);
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate Python source for '{routerFileName}'.", ex);
                }
            }

            var mainPyPath = await GenerateMainPy(projectPath, apisForGroup);
            generatedFiles.Add(mainPyPath);

            return generatedFiles;
        }

        private async Task GenerateServiceStub(string className, JsonElement api, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# This is a generated stub for the {className} service.");
            sb.AppendLine("# In a real application, this would be replaced by a dependency");
            sb.AppendLine("# that contains the actual business logic.");
            sb.AppendLine();
            sb.AppendLine("class " + className + ":");
            sb.AppendLine("    def __init__(self):");
            sb.AppendLine("        pass");
            sb.AppendLine();

            foreach (var method in api.GetProperty("methods").EnumerateArray())
            {
                var methodName = method.GetProperty("methodName").GetString();
                var parameters = method.GetProperty("parameters").EnumerateArray().ToList();
                var paramSignature = string.Join(", ", parameters.Select(p => p.GetProperty("name").GetString()));
                if (!string.IsNullOrEmpty(paramSignature)) paramSignature = ", " + paramSignature;

                sb.AppendLine($"    async def {methodName}(self{paramSignature}):");
                sb.AppendLine($"        return {{'message': 'This is a stub response from {className}.{methodName}'}}");
                sb.AppendLine();
            }
            await File.WriteAllTextAsync(filePath, sb.ToString());
        }

        private async Task<string> GenerateMainPy(string projectPath, List<JsonElement> apis)
        {
            var filePath = Path.Combine(projectPath, "main.py");
            var sb = new StringBuilder();

            sb.AppendLine("from fastapi import FastAPI");
            sb.AppendLine();

            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                if (string.IsNullOrEmpty(className)) continue;
                sb.AppendLine($"from routers import {className.ToLowerInvariant()}_router");
            }

            sb.AppendLine();
            sb.AppendLine("app = FastAPI(title=\"3SC Generated API Shim\")");
            sb.AppendLine();

            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                if (string.IsNullOrEmpty(className)) continue;
                var endpointRoute = api.GetProperty("endpointAttributes")[0].GetProperty("arguments")[0].GetString();
                sb.AppendLine($"app.include_router({className.ToLowerInvariant()}_router.router, prefix='{endpointRoute}', tags=['{className}'])");
            }

            sb.AppendLine();
            sb.AppendLine("@app.get('/')");
            sb.AppendLine("async def root():");
            sb.AppendLine("    return {'message': 'API Shim is running. Visit /docs for OpenAPI documentation.'}");
            sb.AppendLine();

            await File.WriteAllTextAsync(filePath, sb.ToString());
            return filePath;
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var filePath = Path.Combine(projectPath, "requirements.txt");
            _logger.LogInformation("Generating Python requirements file (requirements.txt): {Path}", filePath);

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("fastapi>=0.109.0");
                sb.AppendLine("uvicorn[standard]>=0.27.0");

                if (groupConfig.TryGetProperty("dependencies", out var deps) && deps.TryGetProperty("packages", out var packages))
                {
                    foreach (var prop in packages.EnumerateObject())
                    {
                        sb.AppendLine($"{prop.Name}{prop.Value.GetString()}");
                    }
                }

                await File.WriteAllTextAsync(filePath, sb.ToString());
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate requirements.txt file at '{Path}'.", filePath);
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate Python project file '{filePath}'.", ex);
            }
        }
    }
}