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
    /// Implements the ILanguageGenerator contract for the TypeScript language using Express.
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
            var controllersPath = Path.Combine(srcPath, "controllers");
            var servicesPath = Path.Combine(srcPath, "services");
            Directory.CreateDirectory(srcPath);
            Directory.CreateDirectory(controllersPath);
            Directory.CreateDirectory(servicesPath);

            foreach (var api in apisForGroup)
            {
                var className = api.GetProperty("className").GetString();
                if (string.IsNullOrEmpty(className)) continue;

                try
                {
                    // Step 1: Generate the service stub (placeholder for real business logic)
                    var serviceFilePath = Path.Combine(servicesPath, $"{className}.service.ts");
                    await GenerateServiceStub(className, api, serviceFilePath);
                    generatedFiles.Add(serviceFilePath);

                    // Step 2: Generate the controller that depends on the service
                    var controllerName = $"{className}Controller";
                    var controllerFilePath = Path.Combine(controllersPath, $"{controllerName}.ts");
                    await GenerateController(className, api, controllerFilePath);
                    generatedFiles.Add(controllerFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate source code for class '{ClassName}'.", className);
                    throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate TypeScript source for '{className}'.", ex);
                }
            }

            // Step 3: Generate the central dependency container
            var diContainerPath = Path.Combine(srcPath, "dependencyContainer.ts");
            await GenerateDependencyContainer(diContainerPath, apisForGroup);
            generatedFiles.Add(diContainerPath);

            // Step 4: Generate the main application entry point (index.ts)
            var mainIndexPath = Path.Combine(srcPath, "index.ts");
            await GenerateMainIndexTs(mainIndexPath, apisForGroup);
            generatedFiles.Add(mainIndexPath);

            // Step 5: Generate the TypeScript configuration file (tsconfig.json)
            var tsConfigPath = Path.Combine(projectPath, "tsconfig.json");
            await GenerateTsConfig(tsConfigPath);
            generatedFiles.Add(tsConfigPath);

            return generatedFiles;
        }

        private async Task GenerateController(string className, JsonElement api, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("import { Request, Response, Router } from 'express';");
            sb.AppendLine($"import {{ {className} }} from '../services/{className}.service';");
            sb.AppendLine($"import {{ container }} from '../dependencyContainer';");
            sb.AppendLine();
            sb.AppendLine("export const router = Router();");
            sb.AppendLine($"const service = container.resolve<{className}>('{className}');");
            sb.AppendLine();
            sb.AppendLine($"// --- Routes for {className} ---");
            sb.AppendLine();

            foreach (var method in api.GetProperty("methods").EnumerateArray())
            {
                var httpVerb = method.GetProperty("httpAttribute").GetProperty("type").GetString()?.ToLowerInvariant() ?? "get";
                var route = method.GetProperty("httpAttribute").GetProperty("route").GetString() ?? "/";
                var methodName = method.GetProperty("methodName").GetString();
                var parameters = method.GetProperty("parameters").EnumerateArray().ToList();

                var paramNames = string.Join(", ", parameters.Select(p => p.GetProperty("name").GetString()));

                sb.AppendLine($"router.{httpVerb}('{route}', async (req: Request, res: Response) => {{");
                sb.AppendLine("    try {");
                sb.AppendLine($"        // NOTE: Parameter extraction from req.body, req.params, or req.query is required.");
                sb.AppendLine($"        const result = await service.{methodName}(/* pass parameters here: {paramNames} */);");
                sb.AppendLine($"        res.json(result);");
                sb.AppendLine("    } catch (error) {");
                sb.AppendLine("        // Basic error handling");
                sb.AppendLine("        console.error(error);");
                sb.AppendLine("        res.status(500).json({ message: 'An internal server error occurred.' });");
                sb.AppendLine("    }");
                sb.AppendLine("});");
                sb.AppendLine();
            }

            await File.WriteAllTextAsync(filePath, sb.ToString());
        }

        private async Task GenerateServiceStub(string className, JsonElement api, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// This is a generated stub for the {className} service.");
            sb.AppendLine($"// It provides a placeholder for the actual business logic.");
            sb.AppendLine($"export class {className} {{");

            foreach (var method in api.GetProperty("methods").EnumerateArray())
            {
                var methodName = method.GetProperty("methodName").GetString();
                var parameters = method.GetProperty("parameters").EnumerateArray()
                    .Select(p => $"{p.GetProperty("name").GetString()}: any")
                    .ToList();
                var paramSignature = string.Join(", ", parameters);

                sb.AppendLine($"  public async {methodName}({paramSignature}): Promise<any> {{");
                sb.AppendLine($"    console.log('Executing stub for {className}.{methodName}');");
                sb.AppendLine($"    // In a real application, this would call the actual business logic.");
                sb.AppendLine($"    return Promise.resolve({{ message: `This is a stub response from {className}.{methodName}` }});");
                sb.AppendLine("  }");
                sb.AppendLine();
            }
            sb.AppendLine("}");
            await File.WriteAllTextAsync(filePath, sb.ToString());
        }

        private async Task GenerateDependencyContainer(string filePath, List<JsonElement> apis)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// A simple dependency container stub to manage service instances.");
            sb.AppendLine("// For complex applications, consider a dedicated library like InversifyJS or TSyringe.");
            sb.AppendLine();

            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                if (!string.IsNullOrEmpty(className))
                {
                    sb.AppendLine($"import {{ {className} }} from './services/{className}.service';");
                }
            }
            sb.AppendLine();
            sb.AppendLine("interface IContainer {");
            sb.AppendLine("  register<T>(token: string, instance: T): void;");
            sb.AppendLine("  resolve<T>(token: string): T;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("class Container implements IContainer {");
            sb.AppendLine("  private readonly services: Map<string, any> = new Map();");
            sb.AppendLine();
            sb.AppendLine("  public register<T>(token: string, instance: T): void {");
            sb.AppendLine("    this.services.set(token, instance);");
            sb.AppendLine("  }");
            sb.AppendLine();
            sb.AppendLine("  public resolve<T>(token: string): T {");
            sb.AppendLine("    if (!this.services.has(token)) {");
            sb.AppendLine("      throw new Error(`Service not found: ${token}`);");
            sb.AppendLine("    }");
            sb.AppendLine("    return this.services.get(token) as T;");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("export const container = new Container();");
            sb.AppendLine();

            // Register all discovered services
            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                if (!string.IsNullOrEmpty(className))
                {
                    sb.AppendLine($"container.register('{className}', new {className}());");
                }
            }

            await File.WriteAllTextAsync(filePath, sb.ToString());
        }

        private async Task<string> GenerateMainIndexTs(string filePath, List<JsonElement> apis)
        {
            var sb = new StringBuilder();
            sb.AppendLine("import express, { Express } from 'express';");
            sb.AppendLine();

            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                if (!string.IsNullOrEmpty(className))
                {
                    var variableName = $"{className}Router";
                    var fileName = $"{className}Controller";
                    sb.AppendLine($"import {{ router as {variableName} }} from './controllers/{fileName}';");
                }
            }

            sb.AppendLine();
            sb.AppendLine("const app: Express = express();");
            sb.AppendLine("const port = process.env.PORT || 3000;");
            sb.AppendLine();
            sb.AppendLine("app.use(express.json());");
            sb.AppendLine();

            foreach (var api in apis)
            {
                var className = api.GetProperty("className").GetString();
                if (!string.IsNullOrEmpty(className))
                {
                    var variableName = $"{className}Router";
                    var endpointRoute = api.GetProperty("endpointAttributes")[0].GetProperty("arguments")[0].GetString();
                    sb.AppendLine($"app.use('{endpointRoute}', {variableName});");
                }
            }

            sb.AppendLine();
            sb.AppendLine("app.listen(port, () => {");
            sb.AppendLine("  console.log(`[server]: API Shim is running at http://localhost:${port}`);");
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
                    target = "ES2022",
                    esModuleInterop = true,
                    moduleResolution = "node",
                    sourceMap = true,
                    outDir = "dist",
                    rootDir = "src",
                    strict = true,
                    skipLibCheck = true,
                    forceConsistentCasingInFileNames = true
                },
                include = new[] { "src/**/*" },
                exclude = new[] { "node_modules", "**/*.spec.ts" }
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var jsonContent = JsonSerializer.Serialize(tsConfig, jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonContent);
        }

        public async Task<string> GenerateProjectFileAsync(List<JsonElement> apisForGroup, string projectPath, JsonElement groupConfig)
        {
            var filePath = Path.Combine(projectPath, "package.json");
            _logger.LogInformation("Generating Node.js project file (package.json) for TypeScript: {Path}", filePath);

            try
            {
                var dependencies = new Dictionary<string, string> { { "express", "^4.18.2" } };
                var devDependencies = new Dictionary<string, string> {
                    { "typescript", "^5.3.3" },
                    { "@types/express", "^4.17.21" },
                    { "@types/node", "^20.11.20" },
                    { "ts-node", "^10.9.2" },
                    { "nodemon", "^3.0.3" }
                };

                if (groupConfig.TryGetProperty("dependencies", out var deps) && deps.TryGetProperty("packages", out var packages))
                {
                    foreach (var prop in packages.EnumerateObject())
                    {
                        dependencies[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }

                var packageJson = new
                {
                    name = Path.GetFileName(projectPath)?.ToLowerInvariant() ?? "generated-ts-api",
                    version = "1.0.0",
                    description = "Generated TypeScript API Shim by 3SC API Assembler",
                    main = "dist/index.js",
                    scripts = new
                    {
                        start = "node dist/index.js",
                        build = "tsc",
                        dev = "nodemon --watch 'src/**/*.ts' --exec ts-node src/index.ts"
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
                throw new AssemblerException(AssemblerExitCode.GenerationFailure, $"Failed to generate TypeScript project file '{filePath}'.", ex);
            }
        }
    }
}