using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;
namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Implements the ILanguageGenerator contract for the Python language.
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
            Directory.CreateDirectory(routersPath);

            var routerImports = new List<string>();

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                var routerFileName = $"{className.ToLowerInvariant()}_router.py";
                var sb = new StringBuilder();

                sb.AppendLine("from fastapi import APIRouter");
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

                    sb.AppendLine($"@router.{httpVerb}('{route}')");
                    sb.AppendLine($"async def {methodName}():");
                    sb.AppendLine($"    # TODO: Implement call to the real {className}.{methodName} business logic.");
                    sb.AppendLine($"    return {{'message': 'Response from {methodName}'}}");
                    sb.AppendLine();
                }

                var filePath = Path.Combine(routersPath, routerFileName);
                await File.WriteAllTextAsync(filePath, sb.ToString());
                generatedFiles.Add(filePath);
                routerImports.Add($"from .routers import {className.ToLowerInvariant()}_router");
            }

            var mainPyPath = await GenerateMainPy(projectPath, apisForGroup);
            generatedFiles.Add(mainPyPath);

            return generatedFiles;
        }

        private async Task<string> GenerateMainPy(string projectPath, List<JsonElement> apis)
        {
            var filePath = Path.Combine(projectPath, "main.py");
            var sb = new StringBuilder();

            sb.AppendLine("from fastapi import FastAPI");
            sb.AppendLine();

            // Import all generated routers
            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                sb.AppendLine($"from routers import {className.ToLowerInvariant()}_router");
            }

            sb.AppendLine();
            sb.AppendLine("app = FastAPI()");
            sb.AppendLine();

            // Include each imported router
            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                var endpointRoute = api.GetProperty("endpointAttributes")[0].GetProperty("arguments")[0].GetString();
                sb.AppendLine($"app.include_router({className.ToLowerInvariant()}_router, prefix='{endpointRoute}', tags=['{className}'])");
            }

            sb.AppendLine();
            sb.AppendLine("@app.get('/')");
            sb.AppendLine("async def root():");
            sb.AppendLine("    return {'message': 'API Shim is running'}");


            await File.WriteAllTextAsync(filePath, sb.ToString());
            return filePath;
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var filePath = Path.Combine(projectPath, "requirements.txt");
            _logger.LogInformation("Generating Python requirements file: {Path}", filePath);

            var sb = new StringBuilder();
            sb.AppendLine("fastapi>=0.109.0");
            sb.AppendLine("uvicorn>=0.27.0");

            // Injected dependencies from manifest would be added here
            if (groupConfig.TryGetProperty("dependencies", out var deps) && deps.TryGetProperty("packages", out var packages))
            {
                foreach (var prop in packages.EnumerateObject())
                {
                    sb.AppendLine($"{prop.Name}=={prop.Value.GetString()}");
                }
            }

            await File.WriteAllTextAsync(filePath, sb.ToString());
            return filePath;
        }
    }
}