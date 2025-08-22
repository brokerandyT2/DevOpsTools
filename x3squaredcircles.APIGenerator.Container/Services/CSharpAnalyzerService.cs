using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Defines the contract for a service that analyzes source code of a specific language.
    /// </summary>
    public interface ILanguageAnalyzerService
    {
        Task<List<ServiceBlueprint>> AnalyzeSourceAsync(string sourceDirectory);
    }

    /// <summary>
    /// Implements source code analysis for C# using the Roslyn compiler API. It discovers classes
    /// decorated with [FunctionHandler] and methods with [EventSource] to build a language-agnostic blueprint
    /// that captures the full, verbatim signature of the function entry points.
    /// </summary>
    public class CSharpAnalyzerService : ILanguageAnalyzerService
    {
        private readonly IAppLogger _logger;

        public CSharpAnalyzerService(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<List<ServiceBlueprint>> AnalyzeSourceAsync(string sourceDirectory)
        {
            _logger.LogStartPhase("C# Source Code Analysis");
            var blueprints = new List<ServiceBlueprint>();
            var csharpFiles = Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories);

            if (csharpFiles.Length == 0)
            {
                _logger.LogWarning("No C# source files (*.cs) found in the provided directory.");
                _logger.LogEndPhase("C# Source Code Analysis", true);
                return blueprints;
            }

            // Create a Roslyn compilation. This is essential for semantic analysis, which allows us
            // to understand the types of parameters and not just their syntax.
            var compilation = await CreateCompilationAsync(sourceDirectory, csharpFiles);

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync();
                var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

                foreach (var classDecl in classDeclarations)
                {
                    // Find classes marked with our primary [FunctionHandler] attribute.
                    if (GetDslAttribute(classDecl, semanticModel, "FunctionHandler") == null) continue;

                    var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                    if (classSymbol == null) continue;

                    var serviceNameArg = GetDslAttribute(classDecl, semanticModel, "FunctionHandler")?.Arguments["ServiceName"];
                    var serviceName = string.IsNullOrEmpty(serviceNameArg) ? classSymbol.Name.Replace("Handler", "") : serviceNameArg;

                    var blueprint = new ServiceBlueprint
                    {
                        ServiceName = serviceName!,
                        HandlerClassFullName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    };

                    var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
                    foreach (var methodDecl in methods)
                    {
                        var eventSourceAttr = GetDslAttribute(methodDecl, semanticModel, "EventSource");
                        if (eventSourceAttr == null) continue;

                        var triggerMethod = ParseTriggerMethod(methodDecl, semanticModel, classSymbol);
                        if (triggerMethod != null)
                        {
                            blueprint.TriggerMethods.Add(triggerMethod);
                        }
                    }

                    if (blueprint.TriggerMethods.Any())
                    {
                        blueprints.Add(blueprint);
                        _logger.LogDebug($"Discovered Function Handler: {blueprint.ServiceName} in class {classSymbol.Name} with {blueprint.TriggerMethods.Count} entry point(s).");
                    }
                }
            }

            if (!blueprints.Any())
            {
                _logger.LogWarning("C# analysis complete, but no classes decorated with [FunctionHandler] were found.");
            }
            else
            {
                _logger.LogInfo($"✓ C# analysis complete. Found {blueprints.Count} service(s) to generate.");
            }

            _logger.LogEndPhase("C# Source Code Analysis", true);
            return blueprints;
        }

        private async Task<Compilation> CreateCompilationAsync(string sourceDirectory, string[] csharpFiles)
        {
            var syntaxTrees = new List<SyntaxTree>();
            foreach (var file in csharpFiles)
            {
                var content = await File.ReadAllTextAsync(file);
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(content, path: file));
            }

            // We must provide the compiler with references to understand external types (like from NuGet packages).
            // Start with the core .NET assemblies.
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location)
            };

            // Attempt to find compiled assemblies (.dlls) in the source directory. This is crucial for
            // resolving types from the developer's own projects and dependencies.
            var dllFiles = Directory.GetFiles(sourceDirectory, "*.dll", SearchOption.AllDirectories);
            foreach (var dll in dllFiles)
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(dll));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Could not load reference assembly '{Path.GetFileName(dll)}': {ex.Message}");
                }
            }

            return CSharpCompilation.Create("AssemblerAnalyzerAssembly",
                syntaxTrees: syntaxTrees,
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        /// <summary>
        /// Parses a MethodDeclarationSyntax to create a language-agnostic TriggerMethod model.
        /// </summary>
        private TriggerMethod? ParseTriggerMethod(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel, INamedTypeSymbol classSymbol)
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
            if (methodSymbol == null) return null;

            var triggerMethod = new TriggerMethod
            {
                MethodName = methodDecl.Identifier.ValueText,
                ReturnType = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            };

            // Capture all attributes on the method itself, preserving their full syntax.
            triggerMethod.Attributes.AddRange(
                methodDecl.AttributeLists.SelectMany(al => al.Attributes)
                .Select(attr => new AttributeDefinition
                {
                    Name = attr.Name.ToString(),
                    FullSyntax = attr.ToFullString().Trim()
                }));

            // Capture all 3SC DSL attributes ([EventSource], [Requires], etc.)
            triggerMethod.DslAttributes.AddRange(GetAllDslAttributes(methodDecl, semanticModel));


            // Analyze each parameter in the method's signature.
            foreach (var paramSyntax in methodDecl.ParameterList.Parameters)
            {
                var paramSymbol = semanticModel.GetDeclaredSymbol(paramSyntax);
                if (paramSymbol == null) continue;

                var parameterDef = new ParameterDefinition
                {
                    Name = paramSyntax.Identifier.ValueText,
                    TypeFullName = paramSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    // A parameter is a business logic dependency if it's an interface and NOT a known trigger input type.
                    // This heuristic can be expanded with more sophisticated checks.
                    IsBusinessLogicDependency = paramSymbol.Type.TypeKind == TypeKind.Interface && !IsKnownTriggerType(paramSymbol.Type)
                };

                // Capture all attributes on the parameter, e.g., [HttpTrigger], [FromBody].
                parameterDef.Attributes.AddRange(
                    paramSyntax.AttributeLists.SelectMany(al => al.Attributes)
                    .Select(attr => new AttributeDefinition
                    {
                        Name = attr.Name.ToString(),
                        FullSyntax = attr.ToFullString().Trim()
                    }));

                triggerMethod.Parameters.Add(parameterDef);
            }

            return triggerMethod;
        }

        /// <summary>
        /// Checks if a type is a known, common input type for a serverless trigger,
        /// which helps distinguish them from injectable business logic services.
        /// </summary>
        private bool IsKnownTriggerType(ITypeSymbol typeSymbol)
        {
            var fullTypeName = typeSymbol.ToDisplayString();
            // This list can be expanded to include types from various cloud SDKs (S3Event, EventGridEvent, etc.)
            return fullTypeName.Contains("HttpRequestData") ||
                   fullTypeName.Contains("ILambdaContext") ||
                   fullTypeName.Contains("SQSEvent") ||
                   fullTypeName.Contains("TimerInfo") ||
                   typeSymbol.BaseType?.ToString() == "System.ValueType" || // Structs
                   fullTypeName == "string"; // Primitives
        }

        /// <summary>
        /// Parses a 3SC DSL attribute (like [EventSource]) into a structured DslAttributeDefinition object.
        /// </summary>
        private DslAttributeDefinition? GetDslAttribute(MemberDeclarationSyntax member, SemanticModel model, string attributeName)
        {
            var attributeSyntax = member.AttributeLists
                .SelectMany(list => list.Attributes)
                .FirstOrDefault(attr => attr.Name.ToString() == attributeName || attr.Name.ToString() == $"{attributeName}Attribute");

            if (attributeSyntax == null) return null;

            var dslAttribute = new DslAttributeDefinition { Name = attributeName };
            if (attributeSyntax.ArgumentList != null)
            {
                foreach (var arg in attributeSyntax.ArgumentList.Arguments)
                {
                    var argName = arg.NameEquals?.Name.Identifier.ValueText ?? "0"; // Positional argument
                    var value = model.GetConstantValue(arg.Expression);
                    dslAttribute.Arguments[argName] = value.HasValue ? value.Value?.ToString() ?? string.Empty : arg.Expression.ToString();
                }
            }
            // Handle constructor arguments not having NameEquals
            if (dslAttribute.Arguments.ContainsKey("0") && attributeName == "EventSource")
            {
                dslAttribute.Arguments["EventUrn"] = dslAttribute.Arguments["0"];
                dslAttribute.Arguments.Remove("0");
            }
            if (dslAttribute.Arguments.ContainsKey("0") && attributeName == "FunctionHandler")
            {
                dslAttribute.Arguments["ServiceName"] = dslAttribute.Arguments["0"];
                dslAttribute.Arguments.Remove("0");
            }


            return dslAttribute;
        }

        /// <summary>
        /// Gets all 3SC DSL attributes from a member.
        /// </summary>
        private IEnumerable<DslAttributeDefinition> GetAllDslAttributes(MemberDeclarationSyntax member, SemanticModel model)
        {
            var dslAttributeNames = new HashSet<string> { "EventSource", "DeploymentGroup", "Requires", "RequiresLogger" };
            var attributes = new List<DslAttributeDefinition>();

            foreach (var name in dslAttributeNames)
            {
                var attr = GetDslAttribute(member, model, name);
                if (attr != null)
                {
                    attributes.Add(attr);
                }
            }
            return attributes;
        }
    }
}