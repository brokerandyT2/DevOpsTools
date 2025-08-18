using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text.Json;
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
    /// Uses Roslyn to scan compiled C# assemblies, find classes and methods decorated with the Assembler DSL,
    /// and build a model of the APIs to be generated.
    /// </summary>
    public class DiscoveryService : IDiscoveryService
    {
        private readonly ILogger<DiscoveryService> _logger;

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
                foreach (var path in assemblyPaths)
                {
                    if (!File.Exists(path))
                    {
                        _logger.LogWarning("Library path not found, skipping: {Path}", path);
                        continue;
                    }

                    try
                    {
                        var projectPath = FindProjectFileForAssembly(path);
                        if (projectPath == null)
                        {
                            _logger.LogWarning("Could not find a .csproj for the assembly, analysis may be limited: {Assembly}", path);
                            continue;
                        }

                        var project = await workspace.OpenProjectAsync(projectPath);
                        var compilation = await project.GetCompilationAsync();

                        if (compilation == null)
                        {
                            _logger.LogError("Failed to get compilation for project: {Project}", project.Name);
                            continue;
                        }

                        foreach (var syntaxTree in compilation.SyntaxTrees)
                        {
                            var semanticModel = compilation.GetSemanticModel(syntaxTree);
                            var classDeclarations = (await syntaxTree.GetRootAsync()).DescendantNodes().OfType<ClassDeclarationSyntax>();

                            foreach (var classDecl in classDeclarations)
                            {
                                var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                                if (classSymbol != null && HasAttribute(classSymbol, "ApiEndpointAttribute"))
                                {
                                    var apiModel = BuildApiModel(classSymbol, projectPath);
                                    discoveredApiClasses.Add(apiModel);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to analyze assembly and its project: {Path}", path);
                        throw new AssemblerException(AssemblerExitCode.AssemblyScanFailure, $"Failed to analyze assembly: {path}", ex);
                    }
                }
            }

            var result = new { apiClasses = discoveredApiClasses };
            var jsonString = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            _logger.LogInformation("✓ Discovery complete. Found {Count} API endpoint classes.", discoveredApiClasses.Count);
            return JsonDocument.Parse(jsonString);
        }

        private object BuildApiModel(INamedTypeSymbol classSymbol, string projectPath)
        {
            _logger.LogInformation("Discovered API Endpoint class: {ClassName}", classSymbol.Name);

            var apiModel = new
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

            return apiModel;
        }

        private object BuildMethodModel(IMethodSymbol methodSymbol)
        {
            var methodModel = new
            {
                MethodName = methodSymbol.Name,
                ReturnType = methodSymbol.ReturnType.ToDisplayString(),
                HttpAttribute = GetHttpAttribute(methodSymbol),
                RequiresAttributes = GetAttributes(methodSymbol, "RequiresAttribute"),
                UseTemplatePaths = GetAttributeConstructorArguments(methodSymbol, "UseTemplateAttribute"),
                Parameters = methodSymbol.Parameters.Select(p => new
                {
                    Name = p.Name,
                    Type = p.Type.ToDisplayString()
                }).ToList()
            };

            return methodModel;
        }

        private bool HasAttribute(ISymbol symbol, string attributeName)
        {
            return symbol.GetAttributes().Any(ad => ad.AttributeClass?.Name == attributeName);
        }

        private bool HasHttpAttribute(ISymbol symbol)
        {
            return symbol.GetAttributes().Any(ad => ad.AttributeClass?.BaseType?.Name == "HttpOperationAttribute");
        }

        private string? GetAttributeConstructorArgument(ISymbol symbol, string attributeName)
        {
            var attribute = symbol.GetAttributes().FirstOrDefault(ad => ad.AttributeClass?.Name == attributeName);
            return attribute?.ConstructorArguments.FirstOrDefault().Value?.ToString();
        }

        private List<string?> GetAttributeConstructorArguments(ISymbol symbol, string attributeName)
        {
            return symbol.GetAttributes()
                         .Where(ad => ad.AttributeClass?.Name == attributeName)
                         .Select(ad => ad.ConstructorArguments.FirstOrDefault().Value?.ToString())
                         .ToList();
        }

        private object GetHttpAttribute(ISymbol symbol)
        {
            var attribute = symbol.GetAttributes().First(ad => ad.AttributeClass?.BaseType?.Name == "HttpOperationAttribute");
            return new
            {
                Type = attribute.AttributeClass?.Name.Replace("Attribute", ""),
                Route = attribute.ConstructorArguments.FirstOrDefault().Value?.ToString()
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
            if (assemblyDir == null) return null;

            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            var currentDir = new DirectoryInfo(assemblyDir);
            while (currentDir != null)
            {
                var projectFiles = Directory.GetFiles(currentDir.FullName, $"{assemblyName}.csproj");
                if (projectFiles.Any())
                {
                    return projectFiles.First();
                }
                currentDir = currentDir.Parent;
            }

            return Directory.GetFiles(assemblyDir, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        }
    }
}