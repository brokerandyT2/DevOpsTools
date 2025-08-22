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
    /// Implements the ILanguageGenerator contract for the JavaScript language using Express.
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
            var servicesPath = Path.Combine(projectPath, "services");
            Directory.CreateDirectory(routesPath);
            Directory.CreateDirectory(servicesPath);

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                if (string.IsNullOrEmpty(className)) continue;

                try
                {
                    // 1. Generate the service stub
                    var serviceFilePath = Path.Combine(servicesPath, $"{className}.service.js");
                    await GenerateServiceStub(className, api, serviceFilePath);
                    generatedFiles.Add(serviceFilePath);

                    // 2. Generate the router
                    var routerFileName = $"{className}.router.js";
                    var routerFilePath = Path.Combine(routesPath, routerFileName);
                    await GenerateRouter(className, api, routerFilePath);
                    generatedFiles.Add(routerFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate source code for class '{ClassName}'.", className);
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate JavaScript source for '{className}'.", ex);
                }
            }

            // 3. Generate the central DI container
            var diContainerPath = Path.Combine(projectPath, "dependencyContainer.js");
            await GenerateDependencyContainer(diContainerPath, apisForGroup);
            generatedFiles.Add(diContainerPath);

            // 4. Generate the main application entry point (index.js)
            var mainIndexPath = Path.Combine(projectPath, "index.js");
            await GenerateMainIndexJs(mainIndexPath, apisForGroup);
            generatedFiles.Add(mainIndexPath);

            return generatedFiles;
        }

        private async Task GenerateRouter(string className, JsonElement api, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("const express = require('express');");
            sb.AppendLine("const { container } = require('../dependencyContainer');");
            sb.AppendLine();
            sb.AppendLine("const router = express.Router();");
            sb.AppendLine($"const service = container.resolve('{className}');");
            sb.AppendLine();
            sb.AppendLine($"// --- Routes for {className} ---");
            sb.AppendLine();

            foreach (var method in api.GetProperty("methods").EnumerateArray())
            {
                var httpVerb = method.GetProperty("httpAttribute").GetProperty("type").GetString()?.ToLowerInvariant() ?? "get";
                var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "/";
                var methodName = method.GetProperty("methodName").GetString();

                sb.AppendLine($"router.{httpVerb}('{route}', async (req, res) => {{");
                sb.AppendLine("  try {");
                sb.AppendLine($"    // NOTE: Parameter extraction from req.body, req.params, or req.query is required.");
                sb.AppendLine($"    const result = await service.{methodName}(); // Pass parameters here");
                sb.AppendLine("    res.json(result);");
                sb.AppendLine("  } catch (error) {");
                sb.AppendLine("    console.error(error);");
                sb.AppendLine("    res.status(500).json({ message: 'An internal server error occurred.' });");
                sb.AppendLine("  }");
                sb.AppendLine("});");
                sb.AppendLine();
            }

            sb.AppendLine("module.exports = router;");

            await File.WriteAllTextAsync(filePath, sb.ToString());
        }

        private async Task GenerateServiceStub(string className, JsonElement api, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// This is a generated stub for the {className} service.");
            sb.AppendLine();
            sb.AppendLine($"class {className} {{");

            foreach (var method in api.GetProperty("methods").EnumerateArray())
            {
                var methodName = method.GetProperty("methodName").GetString();
                var paramNames = string.Join(", ", method.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()));

                sb.AppendLine($"  async {methodName}({paramNames}) {{");
                sb.AppendLine($"    console.log('Executing stub for {className}.{methodName}');");
                sb.AppendLine($"    return Promise.resolve({{ message: `This is a stub response from {className}.{methodName}` }});");
                sb.AppendLine("  }");
            }
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"module.exports = {{ {className} }};");
            await File.WriteAllTextAsync(filePath, sb.ToString());
        }

        private async Task GenerateDependencyContainer(string filePath, List<JsonElement> apis)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// A simple dependency container stub to manage service instances.");
            sb.AppendLine();

            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                if (!string.IsNullOrEmpty(className))
                {
                    sb.AppendLine($"const {{ {className} }} = require('./services/{className}.service.js');");
                }
            }
            sb.AppendLine();
            sb.AppendLine("class Container {");
            sb.AppendLine("  constructor() { this.services = new Map(); }");
            sb.AppendLine("  register(token, instance) { this.services.set(token, instance); }");
            sb.AppendLine("  resolve(token) {");
            sb.AppendLine("    if (!this.services.has(token)) { throw new Error(`Service not found: ${token}`); }");
            sb.AppendLine("    return this.services.get(token);");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("const container = new Container();");

            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                if (!string.IsNullOrEmpty(className))
                {
                    sb.AppendLine($"container.register('{className}', new {className}());");
                }
            }
            sb.AppendLine();
            sb.AppendLine("module.exports = { container };");

            await File.WriteAllTextAsync(filePath, sb.ToString());
        }

        private async Task<string> GenerateMainIndexJs(string filePath, List<JsonElement> apis)
        {
            var sb = new StringBuilder();
            sb.AppendLine("const express = require('express');");
            sb.AppendLine("const app = express();");
            sb.AppendLine("const port = process.env.PORT || 3000;");
            sb.AppendLine();
            sb.AppendLine("app.use(express.json());");
            sb.AppendLine();

            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                if (string.IsNullOrEmpty(className)) continue;

                var variableName = $"{className.ToLower()}Router`;";
                var routerFile = $"{className}.router.js`;";
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
            _logger.LogInformation("Generating Node.js project file (package.json): {Path}", filePath);

            try
            {
                var dependencies = new Dictionary<string, string> { { "express", "^4.18.2" } };
                var devDependencies = new Dictionary<string, string> { { "nodemon", "^3.0.3" } };

                if (groupConfig.TryGetProperty("dependencies", out var deps) && deps.TryGetProperty("packages", out var packages))
                {
                    foreach (var prop in packages.EnumerateObject())
                    {
                        dependencies[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }

                var packageJson = new
                {
                    name = Path.GetFileName(projectPath)?.ToLowerInvariant() ?? "generated-js-api",
                    version = "1.0.0",
                    description = "Generated JavaScript API Shim by 3SC API Assembler",
                    main = "index.js",
                    scripts = new
                    {
                        start = "node index.js",
                        dev = "nodemon index.js"
                    },
                    author = "3 Squared Circles API Assembler",
                    license = "UNLICENSED",
                    dependencies,
                    devDependencies
                };

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var jsonContent = JsonSerializer.Serialize(packageJson, jsonOptions);

                await File.WriteAllTextAsync(filePath, jsonContent);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate package.json file at '{Path}'.", filePath);
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate JavaScript project file '{filePath}'.", ex);
            }
        }
    }
}