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
    /// Implements the ILanguageGenerator contract for the Go language.
    /// </summary>
    public class GoGenerator : ILanguageGenerator
    {
        private readonly ILogger<GoGenerator> _logger;

        public GoGenerator(ILogger<GoGenerator> logger)
        {
            _logger = logger;
        }

        public async Task<List<string>> GenerateSourceCodeAsync(List<JsonElement> apisForGroup, string projectPath)
        {
            var generatedFiles = new List<string>();
            var controllersPath = Path.Combine(projectPath, "controllers");
            Directory.CreateDirectory(controllersPath);

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                var controllerFileName = $"{className.ToLowerInvariant()}_controller.go";
                var sb = new StringBuilder();

                sb.AppendLine("package controllers");
                sb.AppendLine();
                sb.AppendLine("import (");
                sb.AppendLine("    \"fmt\"");
                sb.AppendLine("    \"net/http\"");
                sb.AppendLine(")");
                sb.AppendLine();
                sb.AppendLine($"// --- Controller for {className} ---");
                sb.AppendLine();

                foreach (var method in api.GetProperty("methods").EnumerateArray())
                {
                    var methodName = method.GetProperty("methodName").GetString();

                    sb.AppendLine($"func {methodName}Handler(w http.ResponseWriter, r *http.Request) {{");
                    sb.AppendLine($"    // TODO: Implement call to the real {className}.{methodName} business logic.");
                    sb.AppendLine($"    fmt.Fprintf(w, \"Response from {methodName}\")");
                    sb.AppendLine("}");
                    sb.AppendLine();
                }

                var filePath = Path.Combine(controllersPath, controllerFileName);
                await File.WriteAllTextAsync(filePath, sb.ToString());
                generatedFiles.Add(filePath);
            }

            var mainGoPath = await GenerateMainGo(projectPath, apisForGroup);
            generatedFiles.Add(mainGoPath);

            return generatedFiles;
        }

        private async Task<string> GenerateMainGo(string projectPath, List<JsonElement> apis)
        {
            var filePath = Path.Combine(projectPath, "main.go");
            var projectName = Path.GetFileName(projectPath);
            var sb = new StringBuilder();

            sb.AppendLine("package main");
            sb.AppendLine();
            sb.AppendLine("import (");
            sb.AppendLine("    \"fmt\"");
            sb.AppendLine("    \"log\"");
            sb.AppendLine("    \"net/http\"");
            sb.AppendLine($"    \"{projectName}/controllers\"");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("func main() {");

            foreach (var api in apis)
            {
                var endpointRoute = api.GetProperty("endpointAttributes")[0].GetProperty("arguments")[0].GetString();
                foreach (var method in api.GetProperty("methods").EnumerateArray())
                {
                    var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "/";
                    var fullRoute = Path.Combine(endpointRoute, route).Replace("\\", "/");
                    var methodName = method.GetProperty("methodName").GetString();
                    sb.AppendLine($"    http.HandleFunc(\"{fullRoute}\", controllers.{methodName}Handler)");
                }
            }

            sb.AppendLine();
            sb.AppendLine("    port := 8080");
            sb.AppendLine("    fmt.Printf(\"Starting API Shim on port %d\\n\", port)");
            sb.AppendLine("    if err := http.ListenAndServe(fmt.Sprintf(\":%d\", port), nil); err != nil {");
            sb.AppendLine("        log.Fatal(err)");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            await File.WriteAllTextAsync(filePath, sb.ToString());
            return filePath;
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var filePath = Path.Combine(projectPath, "go.mod");
            var projectName = Path.GetFileName(projectPath);
            _logger.LogInformation("Generating Go module file: {Path}", filePath);

            var sb = new StringBuilder();
            sb.AppendLine($"module {projectName}");
            sb.AppendLine();
            sb.AppendLine("go 1.21"); // Version would be configurable
            sb.AppendLine();
            // In a real scenario, dependencies would be added here, e.g., for gorilla/mux

            await File.WriteAllTextAsync(filePath, sb.ToString());
            return filePath;
        }
    }
}