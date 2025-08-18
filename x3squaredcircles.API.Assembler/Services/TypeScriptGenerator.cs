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
    /// Implements the ILanguageGenerator contract for the TypeScript language.
    /// </summary>
    public class TypeScriptGenerator : ILanguageGenerator
    {
        private readonly ILogger<TypeScriptGenerator> _logger;

        public TypeScriptGenerator(ILogger<TypeScriptGenerator> logger)
        {
            _logger = logger;
        }

        public async Task<List<string>> GenerateSourceCodeAsync(List<JsonElement> apisForGroup, string projectPath)
        {
            var generatedFiles = new List<string>();
            var srcPath = Path.Combine(projectPath, "src");
            Directory.CreateDirectory(srcPath);

            var controllerFileNames = new List<string>();

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                var controllerName = $"{className}Controller";
                var controllerFileName = $"{controllerName}.ts";
                var sb = new StringBuilder();

                sb.AppendLine("import { Request, Response, Router } from 'express';");
                sb.AppendLine();
                sb.AppendLine("export const router = Router();");
                sb.AppendLine();
                sb.AppendLine($"// --- Routes for {className} ---");
                sb.AppendLine();

                foreach (var method in api.GetProperty("methods").EnumerateArray())
                {
                    var httpVerb = method.GetProperty("httpAttribute").GetProperty("type").GetString()?.ToLowerInvariant() ?? "get";
                    var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "/";
                    var methodName = method.GetProperty("methodName").GetString();

                    sb.AppendLine($"router.{httpVerb}('{route}', (req: Request, res: Response) => {{");
                    sb.AppendLine($"    // TODO: Implement call to the real {className}.{methodName} business logic.");
                    sb.AppendLine($"    res.json({{ message: `Response from {methodName}` }});");
                    sb.AppendLine("});");
                    sb.AppendLine();
                }

                var filePath = Path.Combine(srcPath, controllerFileName);
                await File.WriteAllTextAsync(filePath, sb.ToString());
                generatedFiles.Add(filePath);
                controllerFileNames.Add(controllerFileName);
            }

            var mainIndexPath = await GenerateMainIndexTs(srcPath, apisForGroup, controllerFileNames);
            generatedFiles.Add(mainIndexPath);

            // Generate tsconfig.json
            var tsConfigPath = Path.Combine(projectPath, "tsconfig.json");
            await GenerateTsConfig(tsConfigPath);
            generatedFiles.Add(tsConfigPath);

            return generatedFiles;
        }

        private async Task<string> GenerateMainIndexTs(string srcPath, List<JsonElement> apis, List<string> controllerFiles)
        {
            var filePath = Path.Combine(srcPath, "index.ts");
            var sb = new StringBuilder();
            sb.AppendLine("import express, { Express } from 'express';");
            sb.AppendLine();

            // Import all generated routers
            for (int i = 0; i < apis.Count; i++)
            {
                var variableName = $"{apis[i].GetProperty("className").GetString()}Router";
                var fileName = Path.GetFileNameWithoutExtension(controllerFiles[i]);
                sb.AppendLine($"import {{ router as {variableName} }} from './{fileName}';");
            }

            sb.AppendLine();
            sb.AppendLine("const app: Express = express();");
            sb.AppendLine("const port = process.env.PORT || 3000;");
            sb.AppendLine();
            sb.AppendLine("app.use(express.json());");
            sb.AppendLine();

            // Use each imported router
            for (int i = 0; i < apis.Count; i++)
            {
                var api = apis[i];
                var variableName = $"{api.GetProperty("className").GetString()}Router";
                var endpointRoute = api.GetProperty("endpointAttributes")[0].GetProperty("arguments")[0].GetString();
                sb.AppendLine($"app.use('{endpointRoute}', {variableName});");
            }

            sb.AppendLine();
            sb.AppendLine("app.listen(port, () => {");
            sb.AppendLine("  console.log(`API Shim listening on port ${port}`);");
            sb.AppendLine("});");

            await File.WriteAllTextAsync(filePath, sb.ToString());
            return filePath;
        }

        private async Task GenerateTsConfig(string filePath)
        {
            var tsConfig = new
            {
                compilerOptions = new
                {
                    module = "CommonJS",
                    target = "ES2021",
                    esModuleInterop = true,
                    outDir = "dist",
                    rootDir = "src",
                    strict = true
                }
            };
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var jsonContent = JsonSerializer.Serialize(tsConfig, jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonContent);
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var filePath = Path.Combine(projectPath, "package.json");
            _logger.LogInformation("Generating Node.js project file (package.json): {Path}", filePath);

            var dependencies = new Dictionary<string, string> {
                { "express", "^4.17.21" }
            };
            var devDependencies = new Dictionary<string, string> {
                { "typescript", "^5.3.3" },
                { "@types/express", "^4.17.21" },
                { "@types/node", "^20.11.19" },
                { "ts-node", "^10.9.2" }
            };

            var packageJson = new
            {
                name = Path.GetFileName(projectPath).ToLowerInvariant(),
                version = "1.0.0",
                description = "Generated API Shim by 3SC API Assembler",
                main = "dist/index.js",
                scripts = new
                {
                    start = "node dist/index.js",
                    build = "tsc",
                    dev = "ts-node src/index.ts"
                },
                dependencies,
                devDependencies
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var jsonContent = JsonSerializer.Serialize(packageJson, jsonOptions);

            await File.WriteAllTextAsync(filePath, jsonContent);
            return filePath;
        }
    }
}