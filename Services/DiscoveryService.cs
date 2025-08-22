using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;
using Microsoft.CodeAnalysis.MSBuild;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for a service that discovers API definitions from business logic assemblies.
    /// </summary>
    public interface IDiscoveryService
    {
        Task<JsonDocument> DiscoverAsync(string libPaths, JsonDocument manifest);
    }

    /// <summary>
    /// Uses Roslyn and MSBuildWorkspace to scan compiled C# projects, find classes and methods
    /// decorated with the Assembler DSL, and build a model of the APIs to be generated.
    /// </summary>
    public class DiscoveryService : IDiscoveryService
    {
        private readonly ILogger<DiscoveryService> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public DiscoveryService(ILogger<DiscoveryService> logger)
        {
            _logger = logger;
        }

        public async Task<JsonDocument> DiscoverAsync(string libPaths, JsonDocument manifest)
        {
            _logger.LogInformation("Starting discovery of API endpoints from libraries: {Paths}", libPaths);
            var discoveredApiClasses = new List<object>();
            var assemblyPaths = libPaths.Split(';', StringSplitOptions.RemoveEmptyEntries);

            if (!assemblyPaths.Any())
            {
                throw new AssemblerException(AssemblerExitCode.AssemblyScanFailure, "No library paths were provided in ASSEMBLER_LIBS.");
            }

            using (var workspace = MSBuildWorkspace.Create())
            {
                workspace.WorkspaceFailed += (s, e) => _logger.LogWarning("MSBuildWorkspace failed: {Message}", e.Diagnostic.Message);

                foreach (var assemblyPath in assemblyPaths)
                {
                    try
                    {
                        var projectPath = FindProjectFileForAssembly(assemblyPath);
                        if (projectPath == null)
                        {
                            _logger.LogWarning("Could not find a .csproj file associated with the assembly, skipping analysis: {Assembly}", assemblyPath);
                            continue;
                        }

                        _logger.LogDebug("Opening project for analysis: {Project}", projectPath);
                        var project = await workspace.OpenProjectAsync(projectPath);
                        var compilation = await project.GetCompilationAsync();

                        if (compilation == null)
                        {
                            _logger.LogError("Failed to get compilation for project: {Project}", project.Name);
                            continue;
                        }

                        // Check for compilation errors in the user's code
                        var diagnostics = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
                        if (diagnostics.Any())
                        {
                            var errors = string.Join("\n", diagnostics.Select(d => $"  - {d.GetMessage()} ({d.Location.GetLineSpan().Path})"));
                            throw new AssemblerException(AssemblerExitCode.AssemblyScanFailure, $"The business logic project '{project.Name}' has compilation errors and cannot be analyzed:\n{errors}");
                        }

                        discoveredApiClasses.AddRange(await AnalyzeCompilation(compilation, projectPath));
                    }
                    catch (AssemblerException) { throw; } // Re-throw our specific exceptions
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An unexpected error occurred while analyzing assembly: {Path}", assemblyPath);
                        throw new AssemblerException(AssemblerExitCode.AssemblyScanFailure, $"An unexpected error occurred while analyzing assembly '{assemblyPath}'.", ex);
                    }
                }
            }

            var result = new { apiClasses = discoveredApiClasses };
            var jsonString = JsonSerializer.Serialize(result, _jsonOptions);

            _logger.LogInformation("✓ Discovery complete. Found {Count} API endpoint classes.", discoveredApiClasses.Count);
            return JsonDocument.Parse(jsonString);
        }

        private async Task<IEnumerable<object>> AnalyzeCompilation(Compilation compilation, string projectPath)
        {
            var apiClasses = new List<object>();
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var classDeclarations = (await syntaxTree.GetRootAsync()).DescendantNodes().OfType<ClassDeclarationSyntax>();

                foreach (var classDecl in classDeclarations)
                {
                    if (semanticModel.GetDeclaredSymbol(classDecl) is INamedTypeSymbol classSymbol &&
                        HasAttribute(classSymbol, "ApiEndpointAttribute"))
                    {
                        apiClasses.Add(BuildApiModel(classSymbol, projectPath));
                    }
                }
            }
            return apiClasses;
        }

        private object BuildApiModel(INamedTypeSymbol classSymbol, string projectPath)
        {
            _logger.LogInformation("  -> Discovered API Endpoint class: {ClassName}", classSymbol.ToDisplayString());
            return new
            {
                ClassName = classSymbol.Name,
                Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
                ProjectPath = projectPath,
                DeploymentGroup = GetAttributeConstructorArgument(classSymbol, "DeployToAttribute"),
                EndpointAttributes = GetAttributes(classSymbol),
                Methods = classSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.MethodKind == MethodKind.Ordinary && HasHttpAttribute(m))
                    .Select(BuildMethodModel)
                    .ToList()
            };
        }

        private object BuildMethodModel(IMethodSymbol methodSymbol) => new
        {
            MethodName = methodSymbol.Name,
            ReturnType = methodSymbol.ReturnType.ToDisplayString(),
            HttpAttribute = GetHttpAttribute(methodSymbol),
            RequiresAttributes = GetAttributes(methodSymbol, "RequiresAttribute"),
            UseTemplatePaths = GetAttributeConstructorArguments(methodSymbol, "UseTemplateAttribute"),
            Parameters = methodSymbol.Parameters.Select(p => new
            {
                p.Name,
                Type = p.Type.ToDisplayString()
            }).ToList()
        };

        private bool HasAttribute(ISymbol symbol, string attributeName) =>
            symbol.GetAttributes().Any(ad => ad.AttributeClass?.Name == attributeName);

        private bool HasHttpAttribute(ISymbol symbol) =>
            symbol.GetAttributes().Any(ad => ad.AttributeClass?.BaseType?.Name == "HttpOperationAttribute");

        private string? GetAttributeConstructorArgument(ISymbol symbol, string attributeName) =>
            symbol.GetAttributes()
                  .FirstOrDefault(ad => ad.AttributeClass?.Name == attributeName)?
                  .ConstructorArguments.FirstOrDefault().Value?.ToString();

        private List<string?> GetAttributeConstructorArguments(ISymbol symbol, string attributeName) =>
            symbol.GetAttributes()
                  .Where(ad => ad.AttributeClass?.Name == attributeName)
                  .Select(ad => ad.ConstructorArguments.FirstOrDefault().Value?.ToString())
                  .ToList();

        private object GetHttpAttribute(ISymbol symbol)
        {
            var attribute = symbol.GetAttributes().First(ad => ad.AttributeClass?.BaseType?.Name == "HttpOperationAttribute");
            return new
            {
                Type = attribute.AttributeClass?.Name.Replace("Attribute", ""),
                Route = attribute.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? "/"
            };
        }

        private List<object> GetAttributes(ISymbol symbol, string? filterByName = null)
        {
            var attributes = symbol.GetAttributes();
            if (filterByName != null)
            {
                attributes = attributes.Where(ad => ad.AttributeClass?.Name == filterByName).ToImmutableArray();
            }

            return attributes.Select(ad => new
            {
                Name = ad.AttributeClass?.Name,
                Arguments = ad.ConstructorArguments.Select(arg => arg.Value?.ToString()).ToList(),
                NamedArguments = ad.NamedArguments.ToDictionary(na => na.Key, na => na.Value.Value?.ToString())
            }).Cast<object>().ToList();
        }

        private string? FindProjectFileForAssembly(string assemblyPath)
        {
            var assemblyDir = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrEmpty(assemblyDir)) return null;

            // Heuristic: Assume the project file lives in a parent directory.
            var currentDir = new DirectoryInfo(assemblyDir);
            while (currentDir != null)
            {
                var projectFiles = currentDir.GetFiles("*.csproj");
                if (projectFiles.Any())
                {
                    return projectFiles.First().FullName;
                }
                currentDir = currentDir.Parent;
            }

            _logger.LogWarning("Could not find project file for assembly '{Assembly}' by traversing parent directories.", assemblyPath);
            return null;
        }
    }
}