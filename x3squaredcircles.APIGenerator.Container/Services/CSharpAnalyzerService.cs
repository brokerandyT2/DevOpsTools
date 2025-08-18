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
    /// decorated with [DataConsumer] and methods with [Trigger] to build a language-agnostic blueprint.
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

            var syntaxTrees = csharpFiles.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file));

            // Create a list of metadata references. This is critical for the semantic model to understand types.
            // Start with the core .NET assemblies.
            var references = new List<MetadataReference>
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location)
    };

            // In a real pipeline, the developer's compiled business logic DLL would be available.
            // We simulate this by assuming it's in a 'bin' directory relative to the source.
            // This is a pragmatic step to ensure the semantic model can resolve the developer's custom types.
            var binDirectory = Path.Combine(sourceDirectory, "bin", "Debug", "net8.0"); // A common convention
            if (Directory.Exists(binDirectory))
            {
                var refAssemblies = Directory.GetFiles(binDirectory, "*.dll");
                references.AddRange(refAssemblies.Select(dll => MetadataReference.CreateFromFile(dll)));
            }

            // Create a Roslyn compilation of all C# files in the directory.
            var compilation = CSharpCompilation.Create("DataLinkAnalyzerAssembly",
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(references)
                .AddSyntaxTrees(syntaxTrees);

            foreach (var syntaxTree in syntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync();
                var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

                foreach (var classDecl in classDeclarations)
                {
                    if (GetAttribute(classDecl, "DataConsumer") == null) continue;

                    var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                    if (classSymbol == null) continue;

                    var serviceName = GetAttributeNamedArgument(GetAttribute(classDecl, "DataConsumer"), "ServiceName") ??
                                      classSymbol.Name.Replace("Service", "").Replace("Processor", "");

                    var blueprint = new ServiceBlueprint
                    {
                        ServiceName = serviceName,
                        HandlerClassFullName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    };

                    var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
                    foreach (var methodDecl in methods)
                    {
                        if (!GetAttributes(methodDecl, "Trigger").Any()) continue;

                        var triggerMethod = ParseTriggerMethod(methodDecl, semanticModel, classSymbol);
                        blueprint.TriggerMethods.Add(triggerMethod);
                    }

                    if (blueprint.TriggerMethods.Any())
                    {
                        blueprints.Add(blueprint);
                        _logger.LogDebug($"Discovered DataConsumer: {blueprint.ServiceName} in class {classSymbol.Name}");
                    }
                }
            }

            if (!blueprints.Any())
            {
                _logger.LogWarning("C# analysis complete, but no classes decorated with [DataConsumer] were found.");
            }
            else
            {
                _logger.LogInfo($"✓ C# analysis complete. Found {blueprints.Count} services to generate.");
            }

            _logger.LogEndPhase("C# Source Code Analysis", true);
            return blueprints;
        }

        private TriggerMethod ParseTriggerMethod(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel, INamedTypeSymbol classSymbol)
        {
            var triggerMethod = new TriggerMethod
            {
                MethodName = methodDecl.Identifier.ValueText,
                ReturnType = methodDecl.ReturnType.ToString(),
                HandlerClassFullName = classSymbol.ToDisplayString()
            };

            foreach (var triggerAttr in GetAttributes(methodDecl, "Trigger"))
            {
                triggerMethod.Triggers.Add(ParseAttribute(triggerAttr).AsTrigger());
            }

            var hookAttributes = GetAttributes(methodDecl, "Requires")
                .Concat(GetAttributes(methodDecl, "RequiresLogger"))
                .Concat(GetAttributes(methodDecl, "RequiresResultsLogger"));

            foreach (var hookAttr in hookAttributes)
            {
                triggerMethod.RequiredHooks.Add(ParseAttribute(hookAttr).AsHook());
            }

            bool isFirstParam = true;
            foreach (var paramSyntax in methodDecl.ParameterList.Parameters)
            {
                var symbol = semanticModel.GetDeclaredSymbol(paramSyntax);
                triggerMethod.Parameters.Add(new ParameterDefinition
                {
                    Name = paramSyntax.Identifier.ValueText,
                    TypeFullName = symbol?.Type?.ToDisplayString() ?? paramSyntax.Type?.ToString() ?? "dynamic",
                    IsPayload = isFirstParam
                });
                isFirstParam = false;
            }
            return triggerMethod;
        }

        private IEnumerable<AttributeSyntax> GetAttributes(MemberDeclarationSyntax member, string attributeName)
        {
            return member.AttributeLists.SelectMany(list => list.Attributes)
                .Where(attr => attr.Name.ToString() == attributeName || attr.Name.ToString() == $"{attributeName}Attribute");
        }

        private AttributeSyntax? GetAttribute(MemberDeclarationSyntax member, string attributeName)
        {
            return GetAttributes(member, attributeName).FirstOrDefault();
        }

        private string? GetAttributeNamedArgument(AttributeSyntax? attribute, string argumentName)
        {
            if (attribute == null) return null;
            var arg = attribute.ArgumentList?.Arguments
                .FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == argumentName);

            return arg?.Expression is LiteralExpressionSyntax literal ? literal.Token.ValueText : null;
        }

        private (string Name, string Value)[] ParseAttributeArguments(AttributeSyntax attribute)
        {
            if (attribute.ArgumentList == null) return Array.Empty<(string, string)>();

            var args = new List<(string Name, string Value)>();
            int positionalIndex = 0;
            foreach (var arg in attribute.ArgumentList.Arguments)
            {
                var name = arg.NameEquals?.Name.Identifier.ValueText ?? GetPositionalArgumentName(attribute.Name.ToString(), positionalIndex++);
                var value = arg.Expression.ToString();

                if (arg.Expression is LiteralExpressionSyntax literal) value = literal.Token.ValueText;
                else if (arg.Expression is TypeOfExpressionSyntax typeOf) value = typeOf.Type.ToString();
                else if (value.Contains('.')) value = value.Split('.').Last();

                args.Add((name, value));
            }
            return args.ToArray();
        }

        private string GetPositionalArgumentName(string attributeName, int index)
        {
            // By convention, map positional arguments to their most likely property name.
            if (attributeName.Contains("Trigger"))
            {
                return index == 0 ? "Type" : "Name";
            }
            return index == 0 ? "Handler" : "Method";
        }

        private ParsedAttribute ParseAttribute(AttributeSyntax attribute)
        {
            return new ParsedAttribute(attribute.Name.ToString().Replace("Attribute", ""), ParseAttributeArguments(attribute));
        }

        private record ParsedAttribute(string Name, (string Name, string Value)[] Arguments)
        {
            public TriggerDefinition AsTrigger()
            {
                var props = Arguments.Where(a => a.Name != "Type" && a.Name != "Name").ToDictionary(a => a.Name, a => a.Value);
                return new TriggerDefinition
                {
                    Type = Arguments.FirstOrDefault(a => a.Name == "Type").Value ?? string.Empty,
                    Name = Arguments.FirstOrDefault(a => a.Name == "Name").Value ?? string.Empty,
                    Properties = props
                };
            }

            public HookDefinition AsHook()
            {
                return new HookDefinition
                {
                    HookType = this.Name,
                    HandlerClassFullName = Arguments.FirstOrDefault(a => a.Name == "Handler" || a.Name == "Contract").Value ?? string.Empty,
                    HandlerMethodName = Arguments.FirstOrDefault(a => a.Name == "Method").Value ?? string.Empty,
                    LogAction = Arguments.FirstOrDefault(a => a.Name == "Action").Value,
                    TraceVariableName = Arguments.FirstOrDefault(a => a.Name == "Variable").Value
                };
            }
        }
    }
}