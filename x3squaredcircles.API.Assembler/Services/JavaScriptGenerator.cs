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
    /// Implements the ILanguageGenerator contract for the JavaScript language.
    /// </summary>
    public class JavaScriptGenerator : ILanguageGenerator
    {
        private readonly ILogger<JavaScriptGenerator> _logger;

        public JavaScriptGenerator(ILogger<JavaScriptGenerator> logger)
        {
            _logger = logger;
        }

        public async Task<List<string>> GenerateSourceCodeAsync(List<JsonElement> apisForGroup, string projectPath)
        {
            var generatedFiles = new List<string>();
            var routesPath = Path.Combine(projectPath, "routes");
            Directory.CreateDirectory(routesPath);

            var routerFileNames = new List<string>();

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                var endpointRoute = api.GetProperty("endpointAttributes")[0].GetProperty("arguments")[0].GetString();
                var routerFileName = $"{className}.router.js";
                var sb = new StringBuilder();

                sb.AppendLine("const express = require('express');");
                sb.AppendLine("const router = express.Router();");
                sb.AppendLine();
                sb.AppendLine($"// --- Routes for {className} ---");
                sb.AppendLine();

                foreach (var method in api.GetProperty("methods").EnumerateArray())
                {
                    var httpVerb = method.GetProperty("httpAttribute").GetProperty("type").GetString()?.ToLowerInvariant() ?? "get";
                    var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "/";
                    var methodName = method.GetProperty("methodName").GetString();

                    sb.AppendLine($"router.{httpVerb}('{route}', (req, res) => {{");
                    sb.AppendLine($"    // TODO: Implement call to the real {className}.{methodName} business logic.");
                    sb.AppendLine($"    res.json({{ message: 'Response from {methodName}' }});");
                    sb.AppendLine("});");
                    sb.AppendLine();
                }

                sb.AppendLine("module.exports = router;");

                var filePath = Path.Combine(routesPath, routerFileName);
                await File.WriteAllTextAsync(filePath, sb.ToString());
                generatedFiles.Add(filePath);
                routerFileNames.Add(routerFileName);
            }

            // Generate the main index.js to wire up all the routers
            var mainIndexPath = await GenerateMainIndexJs(projectPath, apisForGroup, routerFileNames);
            generatedFiles.Add(mainIndexPath);

            return generatedFiles;
        }

        private async Task<string> GenerateMainIndexJs(string projectPath, List<JsonElement> apis, List<string> routerFiles)
        {
            var filePath = Path.Combine(projectPath, "index.js");
            var sb = new StringBuilder();
            sb.AppendLine("const express = require('express');");
            sb.AppendLine("const app = express();");
            sb.AppendLine("const port = process.env.PORT || 3000;");
            sb.AppendLine();
            sb.AppendLine("app.use(express.json());");
            sb.AppendLine();

            // Require and use each generated router
            for (int i = 0; i < apis.Count; i++)
            {
                var api = apis[i];
                var routerFile = routerFiles[i];
                var variableName = $"{api.GetProperty("className").GetString().ToLower()}Router";
                var endpointRoute = api.GetProperty("endpointAttributes")[0].GetProperty("arguments")[0].GetString();

                sb.AppendLine($"const {variableName} = require('./routes/{routerFile}');");
                sb.AppendLine($"app.use('{endpointRoute}', {variableName});");
            }

            sb.AppendLine();
            sb.AppendLine("app.listen(port, () => {");
            sb.AppendLine("  console.log(`API Shim listening on port ${port}`);");
            sb.AppendLine("});");

            await File.WriteAllTextAsync(filePath, sb.ToString());
            return filePath;
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var filePath = Path.Combine(projectPath, "package.json");
            _logger.LogInformation("Generating Node.js project file: {Path}", filePath);

            var dependencies = new Dictionary<string, string> { { "express", "^4.18.2" } };
            if (groupConfig.TryGetProperty("dependencies", out var deps) && deps.TryGetProperty("packages", out var packages))
            {
                foreach (var prop in packages.EnumerateObject())
                {
                    dependencies[prop.Name] = prop.Value.GetString();
                }
            }

            var packageJson = new
            {
                name = Path.GetFileName(projectPath).ToLowerInvariant(),
                version = "1.0.0",
                description = "Generated API Shim by 3SC API Assembler",
                main = "index.js",
                scripts = new
                {
                    start = "node index.js",
                    test = "echo \"Error: no test specified\" && exit 1"
                },
                dependencies
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var jsonContent = JsonSerializer.Serialize(packageJson, jsonOptions);

            await File.WriteAllTextAsync(filePath, jsonContent);
            return filePath;
        }
    }
}