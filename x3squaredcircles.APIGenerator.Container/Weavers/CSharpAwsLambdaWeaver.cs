using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;
using x3squaredcircles.DataLink.Container.Weavers;

namespace x3squaredcircles.datalink.container.Weavers
{
    /// <summary>
    /// Implements ILanguageWeaver for generating a C# AWS Lambda project using the SAM CLI template.
    /// This is the definitive weaver for the C# on AWS Lambda target.
    /// </summary>
    public class CSharpAwsLambdaWeaver : ILanguageWeaver
    {
        private readonly IAppLogger _logger;
        private readonly ServiceBlueprint _blueprint;

        public CSharpAwsLambdaWeaver(IAppLogger logger, ServiceBlueprint blueprint)
        {
            _logger = logger;
            _blueprint = blueprint;
        }

        public async Task GenerateProjectFileAsync(string projectPath, string logicSourcePath)
        {
            var uniqueTriggers = _blueprint.TriggerMethods
                .SelectMany(tm => tm.Triggers)
                .Select(t => t.Type)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var packageReferences = new StringBuilder();

            // Add required base packages for AWS Lambda .NET
            packageReferences.AppendLine(@"    <PackageReference Include=""Amazon.Lambda.Core"" Version=""2.2.0"" />");
            packageReferences.AppendLine(@"    <PackageReference Include=""Amazon.Lambda.Serialization.SystemTextJson"" Version=""2.4.1"" />");
            packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Extensions.DependencyInjection"" Version=""8.0.0"" />");

            // Dynamically add NuGet packages for the specific event sources
            if (uniqueTriggers.Contains("Http"))
                packageReferences.AppendLine(@"    <PackageReference Include=""Amazon.Lambda.APIGatewayEvents"" Version=""2.7.0"" />");
            if (uniqueTriggers.Contains("AwsSqsQueue"))
                packageReferences.AppendLine(@"    <PackageReference Include=""Amazon.Lambda.SQSEvents"" Version=""2.2.0"" />");

            var logicProjectFilePath = Directory.GetFiles(logicSourcePath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            if (logicProjectFilePath == null)
            {
                throw new DataLinkException(ExitCode.SourceAnalysisFailed, "CSPROJ_NOT_FOUND", "Could not find a .csproj file in the business logic source path.");
            }
            var relativeLogicPath = Path.GetRelativePath(projectPath, logicProjectFilePath);

            var csprojContent = $@"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <AWSProjectType>Lambda</AWSProjectType>
    <RootNamespace>{_blueprint.ServiceName}</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
{packageReferences.ToString().TrimEnd()}
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""{relativeLogicPath}"" />
  </ItemGroup>
</Project>";
            var filePath = Path.Combine(projectPath, $"{_blueprint.ServiceName}.csproj");
            await File.WriteAllTextAsync(filePath, csprojContent.Trim());
            _logger.LogDebug($"Generated C# AWS Lambda project file: {filePath}");
        }

        public async Task GenerateStartupFileAsync(string projectPath)
        {
            var allHandlerClasses = _blueprint.TriggerMethods
                .SelectMany(tm => tm.RequiredHooks.Select(h => h.HandlerClassFullName))
                .Append(_blueprint.HandlerClassFullName)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var diRegistrations = string.Join(Environment.NewLine, allHandlerClasses.Select(cls => $"        services.AddTransient<{cls}>();"));

            var allDependencyInterfaces = _blueprint.TriggerMethods
                .SelectMany(tm => tm.Parameters.Where(p => !p.IsPayload && p.TypeFullName.StartsWith("I")))
                .Select(p => p.TypeFullName)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var dependencyComment = allDependencyInterfaces.Any()
                ? "// NOTE: Register the concrete implementations for your business logic's dependencies below."
                : "// No external dependencies were detected in trigger method signatures.";
            var dependencyRegistrations = string.Join(Environment.NewLine, allDependencyInterfaces.Select(idep => $"        // services.AddSingleton<{idep}, Concrete{idep.Split('.').Last().Substring(1)}>();"));

            var usingStatements = allHandlerClasses.Concat(allDependencyInterfaces)
                .Where(cls => cls.Contains('.'))
                .Select(cls => cls.Substring(0, cls.LastIndexOf('.')))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ns => ns)
                .Select(ns => $"using {ns};");

            var startupContent = $@"
using Microsoft.Extensions.DependencyInjection;
{string.Join(Environment.NewLine, usingStatements)}

// Auto-generated by 3SC DataLink at {DateTime.UtcNow:O}
// Source Version: {_blueprint.Metadata.SourceVersionTag}

namespace {_blueprint.ServiceName};

public class Startup
{{
    public Startup()
    {{
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        ServiceProvider = serviceCollection.BuildServiceProvider();
    }}

    public IServiceProvider ServiceProvider {{ get; }}

    private void ConfigureServices(IServiceCollection services)
    {{
        // Register developer-defined handler and hook classes
{diRegistrations}

        // Register dependencies required by the business logic
        {dependencyComment}
{dependencyRegistrations}
    }}
}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "Startup.cs"), startupContent.Trim());
            _logger.LogDebug($"Generated Startup.cs at: {projectPath}");
        }

        public async Task GeneratePlatformFilesAsync(string projectPath)
        {
            var resources = new StringBuilder();
            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                var trigger = triggerMethod.Triggers.First();
                var handlerPath = $"{_blueprint.ServiceName}::{_blueprint.ServiceName}.{triggerMethod.MethodName}_Function::{triggerMethod.MethodName}";
                var (eventType, eventProperties) = GetSamEvent(trigger);

                resources.AppendLine($@"  {triggerMethod.MethodName}Function:
    Type: AWS::Serverless::Function
    Properties:
      PackageType: Zip
      CodeUri: .
      Handler: {handlerPath}
      Runtime: dotnet8
      Architectures:
        - x86_64
      Events:
        Trigger:
          Type: {eventType}
          Properties:
{eventProperties}");
            }

            var samTemplateContent = $@"
AWSTemplateFormatVersion: '2010-09-09'
Transform: AWS::Serverless-2016-10-31
Description: >
  {_blueprint.ServiceName} - Auto-generated by 3SC DataLink from source version {_blueprint.Metadata.SourceVersionTag}

Resources:
{resources.ToString().TrimEnd()}
";
            var filePath = Path.Combine(projectPath, "template.yaml");
            await File.WriteAllTextAsync(filePath, samTemplateContent.Trim());
            _logger.LogDebug($"Generated template.yaml file: {filePath}");
        }

        public async Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath)
        {
            var payload = triggerMethod.Parameters.First(p => p.IsPayload);
            var dependencies = triggerMethod.Parameters.Where(p => !p.IsPayload).ToList();
            var handlerClassName = _blueprint.HandlerClassFullName.Split('.').Last();
            var handlerVarName = "_" + char.ToLowerInvariant(handlerClassName[0]) + handlerClassName.Substring(1);
            var functionClassName = $"{triggerMethod.MethodName}_Function";

            var (awsEventRequest, payloadAcquisitionLogic) = GetAwsEventTypeAndPayloadLogic(triggerMethod.Triggers.First(), payload);

            var diMap = BuildDependencyMap(triggerMethod, handlerVarName);
            var diDeclarations = string.Join(Environment.NewLine, diMap.Select(kvp => $"    private readonly {kvp.Key} {kvp.Value};"));
            var diAssignments = string.Join(Environment.NewLine, diMap.Select(kvp => $"        {kvp.Value} = startup.ServiceProvider.GetRequiredService<{kvp.Key}>();"));

            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader(diMap.Keys.Append(payload.TypeFullName)));
            sb.AppendLine();
            sb.AppendLine($"namespace {_blueprint.ServiceName};");
            sb.AppendLine();
            sb.AppendLine($"public class {functionClassName}");
            sb.AppendLine("{");
            sb.AppendLine(diDeclarations);
            sb.AppendLine();
            sb.AppendLine("    public " + functionClassName + "()");
            sb.AppendLine("    {");
            sb.AppendLine("        var startup = new Startup();");
            sb.AppendLine(diAssignments);
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public async Task<APIGatewayProxyResponse> {triggerMethod.MethodName}({awsEventRequest} request, ILambdaContext context)");
            sb.AppendLine("    {");
            sb.AppendLine("        try {");
            sb.AppendLine(payloadAcquisitionLogic);
            // Weave hook logic and business logic call here
            sb.AppendLine($"            // Example call to business logic");
            sb.AppendLine($"            await {handlerVarName}.{triggerMethod.MethodName}(payload);");
            sb.AppendLine("            return new APIGatewayProxyResponse { StatusCode = 200, Body = \"Success\" };");
            sb.AppendLine("        }");
            sb.AppendLine("        catch(Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            context.Logger.LogError($\"Error in {functionClassName}: {ex.Message}\");");
            sb.AppendLine("            return new APIGatewayProxyResponse { StatusCode = 500, Body = \"Internal Server Error\" };");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            await File.WriteAllTextAsync(Path.Combine(projectPath, $"{functionClassName}.cs"), sb.ToString());
            _logger.LogDebug($"Generated C# Lambda function file: {functionClassName}.cs");
        }

        public async Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath)
        {
            _logger.LogInfo("Assembling C# AWS Lambda test harness project...");
            var testProjectName = $"{_blueprint.ServiceName}.Tests";
            var relativeMainPath = Path.GetRelativePath(testProjectPath, mainProjectPath);
            var logicProjectFilePath = Directory.GetFiles(testSourcePath, "*.csproj", SearchOption.AllDirectories).First();
            var relativeLogicPath = Path.GetRelativePath(testProjectPath, logicProjectFilePath);

            var testCsprojContent = $@"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.8.0"" />
    <PackageReference Include=""xunit"" Version=""2.5.3"" />
    <PackageReference Include=""Moq"" Version=""4.20.70"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""..\{relativeMainPath}\{_blueprint.ServiceName}.csproj"" />
    <ProjectReference Include=""..\{relativeLogicPath}"" />
  </ItemGroup>
</Project>";
            await File.WriteAllTextAsync(Path.Combine(testProjectPath, $"{testProjectName}.csproj"), testCsprojContent.Trim());

            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var relevantTestFiles = Directory.GetFiles(testSourcePath, $"*{handlerClassNameShort}Tests.cs", SearchOption.AllDirectories);
            foreach (var testFile in relevantTestFiles)
            {
                File.Copy(testFile, Path.Combine(testProjectPath, Path.GetFileName(testFile)), true);
            }

            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                await GenerateSingleTestHarnessFileAsync(triggerMethod, testProjectPath);
            }
        }

        private async Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testProjectPath)
        {
            var functionClassName = $"{triggerMethod.MethodName}_Function";
            var payloadType = triggerMethod.Parameters.First(p => p.IsPayload).TypeFullName;

            var harnessContent = $@"
using Xunit;
using Moq;
using System;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
{GenerateFileHeader(new[] { payloadType, _blueprint.HandlerClassFullName })}
using {_blueprint.ServiceName};

// Auto-generated by 3SC DataLink. This test skeleton is designed to fail by default.
public class {functionClassName}_HarnessTests
{{
    [Fact]
    public void {triggerMethod.MethodName}_HappyPath_ShouldSucceed()
    {{
        // This test will fail here until you implement the mock setups and assertions.
        throw new NotImplementedException(""Test not yet implemented. Please configure mock setups and assertions, then remove this line."");

        /* --- EXAMPLE IMPLEMENTATION ---
        // Arrange
        var function = new {functionClassName}(); // This needs DI to be testable, a limitation of this simple generator
        var request = new APIGatewayProxyRequest {{ Body = ""{{...}}"" }};
        var context = new TestLambdaContext();

        // Act
        // var response = await function.{triggerMethod.MethodName}(request, context);

        // Assert
        // Assert.Equal(200, response.StatusCode);
        */
    }}
}}";
            await File.WriteAllTextAsync(Path.Combine(testProjectPath, $"{functionClassName}_Harness.cs"), harnessContent.Trim());
        }

        private Dictionary<string, string> BuildDependencyMap(TriggerMethod triggerMethod, string handlerVarName)
        {
            var diServices = new Dictionary<string, string>
            {
                { "ILambdaContext", "_context" }, // Lambda context is special
                { _blueprint.HandlerClassFullName, handlerVarName }
            };
            triggerMethod.RequiredHooks.ForEach(h => diServices.TryAdd(h.HandlerClassFullName, $"_{h.HandlerMethodName}_hook"));
            triggerMethod.Parameters.Where(p => !p.IsPayload).ToList().ForEach(d => diServices.TryAdd(d.TypeFullName, $"_{d.Name}_service"));
            return diServices;
        }

        private string GenerateFileHeader(IEnumerable<string> types)
        {
            return string.Join(Environment.NewLine, types
                .Where(t => t.Contains('.'))
                .Select(t => t.Substring(0, t.LastIndexOf('.')))
                .Append("Microsoft.Extensions.DependencyInjection")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ns => ns)
                .Select(ns => $"using {ns};"));
        }

        private (string RequestType, string PayloadLogic) GetAwsEventTypeAndPayloadLogic(TriggerDefinition trigger, ParameterDefinition payload)
        {
            switch (trigger.Type)
            {
                case "Http":
                    return ("APIGatewayProxyRequest", $"var payload = JsonSerializer.Deserialize<{payload.TypeFullName}>(request.Body);");
                case "AwsSqsQueue":
                    return ("SQSEvent", $"var payload = JsonSerializer.Deserialize<{payload.TypeFullName}>(request.Records[0].Body);");
                default:
                    return ("object", $"// Payload deserialization logic for {trigger.Type} needed here.");
            }
        }
    }
}