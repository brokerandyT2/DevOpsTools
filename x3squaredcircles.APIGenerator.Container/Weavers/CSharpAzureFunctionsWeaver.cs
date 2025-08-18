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
    /// Implements ILanguageWeaver for generating a C# Azure Functions project using the Isolated Worker model.
    /// </summary>
    public class CSharpAzureFunctionsWeaver : ILanguageWeaver
    {
        private readonly IAppLogger _logger;
        private readonly ServiceBlueprint _blueprint;

        public CSharpAzureFunctionsWeaver(IAppLogger logger, ServiceBlueprint blueprint)
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

            // Add required base packages for Azure Functions Isolated Worker
            packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker"" Version=""1.21.0"" />");
            packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker.Sdk"" Version=""1.17.0"" />");
            packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.ApplicationInsights.WorkerService"" Version=""2.22.0"" />");

            // Dynamically add NuGet packages for the specific triggers used in the service
            if (uniqueTriggers.Contains("Http"))
                packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker.Extensions.Http"" Version=""3.1.0"" />");
            if (uniqueTriggers.Contains("AzureServiceBusQueue"))
                packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker.Extensions.ServiceBus"" Version=""5.15.0"" />");
            if (uniqueTriggers.Contains("Cron"))
                packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker.Extensions.Timer"" Version=""4.3.0"" />");

            // Find the business logic's .csproj file to create a relative reference
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
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>{_blueprint.ServiceName}</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
{packageReferences.ToString().TrimEnd()}
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""{relativeLogicPath}"" />
  </ItemGroup>
  <ItemGroup>
    <None Update=""host.json"">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update=""local.settings.json"">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>Never</CopyToPublishDirectory>
    </None>
  </ItemGroup>
</Project>";
            var filePath = Path.Combine(projectPath, $"{_blueprint.ServiceName}.csproj");
            await File.WriteAllTextAsync(filePath, csprojContent.Trim());
            _logger.LogDebug($"Generated C# project file: {filePath}");
        }

        public async Task GenerateStartupFileAsync(string projectPath)
        {
            // Discover all unique classes that need to be dependency injected.
            var allRequiredClasses = _blueprint.TriggerMethods
                .SelectMany(tm => tm.RequiredHooks.Select(h => h.HandlerClassFullName))
                .Append(_blueprint.HandlerClassFullName)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var diRegistrations = string.Join(Environment.NewLine, allRequiredClasses.Select(cls => $"        services.AddTransient<{cls}>();"));

            var allDependencyInterfaces = _blueprint.TriggerMethods
                .SelectMany(tm => tm.Parameters.Where(p => !p.IsPayload && p.TypeFullName.StartsWith("I")))
                .Select(p => p.TypeFullName)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var dependencyComment = allDependencyInterfaces.Any()
                ? "// NOTE: Register the concrete implementations for your business logic's dependencies below."
                : "// No external dependencies were detected in trigger method signatures.";
            var dependencyRegistrations = string.Join(Environment.NewLine, allDependencyInterfaces.Select(idep => $"        // services.AddSingleton<{idep}, Concrete{idep.Split('.').Last().Substring(1)}>();"));

            var usingStatements = allRequiredClasses.Concat(allDependencyInterfaces)
                .Where(cls => cls.Contains('.'))
                .Select(cls => cls.Substring(0, cls.LastIndexOf('.')))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ns => ns)
                .Select(ns => $"using {ns};");

            var programContent = $@"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
{string.Join(Environment.NewLine, usingStatements)}

// Auto-generated by 3SC DataLink at {DateTime.UtcNow:O}
// Source Version: {_blueprint.Metadata.SourceVersionTag}

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {{
        // Register developer-defined handler and hook classes
{diRegistrations}

        // Register dependencies required by the business logic
        {dependencyComment}
{dependencyRegistrations}
    }})
    .Build();

host.Run();";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "Program.cs"), programContent.Trim());
            _logger.LogDebug($"Generated Program.cs at: {projectPath}");
        }

        public async Task GeneratePlatformFilesAsync(string projectPath)
        {
            var hostJsonContent = @"{
  ""version"": ""2.0"",
  ""logging"": {
    ""applicationInsights"": {
      ""samplingSettings"": {
        ""isEnabled"": true,
        ""excludedTypes"": ""Request""
      }
    }
  },
  ""extensionBundle"": {
    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
    ""version"": ""[4.*, 5.0.0)""
  }
}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "host.json"), hostJsonContent.Trim());
            _logger.LogDebug($"Generated host.json at: {projectPath}");

            var settingsJsonContent = @"{
  ""IsEncrypted"": false,
  ""Values"": {
    ""AzureWebJobsStorage"": ""UseDevelopmentStorage=true"",
    ""FUNCTIONS_WORKER_RUNTIME"": ""dotnet-isolated""
  }
}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "local.settings.json"), settingsJsonContent.Trim());
            _logger.LogDebug($"Generated local.settings.json at: {projectPath}");
        }

        public async Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath)
        {
            var payload = triggerMethod.Parameters.FirstOrDefault(p => p.IsPayload);
            if (payload == null) throw new DataLinkException(ExitCode.SourceAnalysisFailed, "NO_PAYLOAD_PARAMETER", $"Trigger method '{triggerMethod.MethodName}' must have at least one parameter to act as the payload DTO.");

            var dependencies = triggerMethod.Parameters.Where(p => !p.IsPayload).ToList();
            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var handlerVarName = "_" + char.ToLowerInvariant(handlerClassNameShort[0]) + handlerClassNameShort.Substring(1);
            var functionName = triggerMethod.MethodName;

            var diMap = BuildDependencyMap(triggerMethod, handlerVarName);
            var ctorParams = string.Join(", ", diMap.Select(kvp => $"{kvp.Key} {kvp.Value.TrimStart('_')}"));
            var ctorAssignments = string.Join(Environment.NewLine, diMap.Select(kvp => $"        {kvp.Value} = {kvp.Value.TrimStart('_')};"));

            var (triggerAttribute, functionSignatureParams, payloadAcquisitionLogic) = GenerateTriggerAndPayloadCode(triggerMethod.Triggers.First(), payload);

            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader(diMap.Keys.Append(payload.TypeFullName)));
            sb.AppendLine();
            sb.AppendLine($"namespace {_blueprint.ServiceName};");
            sb.AppendLine();
            sb.AppendLine($"public class {functionName}_Function");
            sb.AppendLine("{");
            foreach (var service in diMap) sb.AppendLine($"    private readonly {service.Key} {service.Value};");
            sb.AppendLine();
            sb.AppendLine($"    public {functionName}_Function({ctorParams})");
            sb.AppendLine("    {");
            sb.AppendLine(ctorAssignments);
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    [Function(\"{functionName}\")]");
            sb.AppendLine($"    public async Task Run({triggerAttribute})");
            sb.AppendLine("    {");
            sb.AppendLine(await GenerateMethodBodyAsync(triggerMethod, payload, dependencies, handlerVarName, diMap, payloadAcquisitionLogic, functionSignatureParams));
            sb.AppendLine("    }");
            sb.AppendLine("}");

            await File.WriteAllTextAsync(Path.Combine(projectPath, $"{functionName}_Function.cs"), sb.ToString());
            _logger.LogDebug($"Generated function file: {functionName}_Function.cs");
        }

        public async Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath)
        {
            _logger.LogInfo("Assembling C# test harness project...");
            var testProjectName = $"{_blueprint.ServiceName}.Tests";
            var logicProjectFilePath = Directory.GetFiles(testProjectPath, "*.csproj", SearchOption.AllDirectories).First();
            var relativeMainPath = Path.GetRelativePath(testProjectPath, mainProjectPath);
            var relativeLogicPath = Path.GetRelativePath(testProjectPath, logicProjectFilePath);

            var testCsprojContent = $@"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.8.0"" />
    <PackageReference Include=""xunit"" Version=""2.5.3"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.5.3"" />
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
                _logger.LogDebug($"Copied developer test file: {Path.GetFileName(testFile)}");
            }

            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                await GenerateSingleTestHarnessFileAsync(triggerMethod, testProjectPath);
            }
        }

        private async Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testProjectPath)
        {
            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var handlerVarName = "_" + char.ToLowerInvariant(handlerClassNameShort[0]) + handlerClassNameShort.Substring(1);
            var functionClassName = $"{triggerMethod.MethodName}_Function";
            var payload = triggerMethod.Parameters.First(p => p.IsPayload);
            var diMap = BuildDependencyMap(triggerMethod, handlerVarName);

            var mockFields = string.Join(Environment.NewLine, diMap.Select(kvp => $"    private readonly Mock<{kvp.Key}> {kvp.Value.Insert(1, "mock")};"));
            var mockInits = string.Join(Environment.NewLine, diMap.Select(kvp => $"        {kvp.Value.Insert(1, "mock")} = new Mock<{kvp.Key}>();"));
            var mockObjects = string.Join(", ", diMap.Select(kvp => $"{kvp.Value.Insert(1, "mock")}.Object"));

            var harnessContent = $@"
using Xunit;
using Moq;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
{GenerateFileHeader(diMap.Keys.Append(payload.TypeFullName))}
using {_blueprint.ServiceName};

// Auto-generated by 3SC DataLink. This test skeleton is designed to fail by default.
public class {functionClassName}_HarnessTests
{{
{mockFields}

    public {functionClassName}_HarnessTests()
    {{
{mockInits}
    }}

    [Fact]
    public void {triggerMethod.MethodName}_HappyPath_ShouldSucceed()
    {{
        // Arrange
        var function = new {functionClassName}({mockObjects});
        var payload = new {payload.TypeFullName}(); // Create a sample payload
        
        // This test will fail here until you implement the mock setups and assertions.
        throw new NotImplementedException(""Test not yet implemented. Please configure mock setups and assertions, then remove this line."");
        
        /* --- EXAMPLE IMPLEMENTATION ---
        // Act
        // await function.Run(...); // Requires mocking the specific trigger input (e.g., HttpRequestData)
        
        // Assert
        // _mock{handlerClassNameShort}.Verify(h => h.{triggerMethod.MethodName}(It.IsAny<{payload.TypeFullName}>(), ...), Times.Once);
        */
    }}
}}";
            await File.WriteAllTextAsync(Path.Combine(testProjectPath, $"{functionClassName}_Harness.cs"), harnessContent.Trim());
        }

        private async Task<string> GenerateMethodBodyAsync(TriggerMethod triggerMethod, ParameterDefinition payload, List<ParameterDefinition> dependencies, string handlerVarName, Dictionary<string, string> diMap, string payloadAcquisitionLogic, string functionSignatureParams)
        {
            var body = new StringBuilder();
            body.AppendLine($"        var context = new TriggerContext(); // This would be populated with trigger-specific metadata");
            body.AppendLine("        try {");
            foreach (var hook in triggerMethod.RequiredHooks.Where(h => h.HookType.StartsWith("Requires") && !h.HookType.Contains("Logger")))
            {
                var hookVarName = diMap[hook.HandlerClassFullName];
                body.AppendLine($"            var authResult = {hookVarName}.{hook.HandlerMethodName}({functionSignatureParams});");
                body.AppendLine("            if (!authResult) { /* return 401 Unauthorized response */ return; }");
            }
            body.AppendLine(payloadAcquisitionLogic);
            foreach (var hook in triggerMethod.RequiredHooks.Where(h => h.HookType == "RequiresLogger" && h.LogAction == "OnInbound"))
            {
                var hookVarName = diMap[hook.HandlerClassFullName];
                body.AppendLine($"            await {hookVarName}.{hook.HandlerMethodName}(payload, context);");
            }
            var diParamNames = string.Join(", ", dependencies.Select(d => diMap[d.TypeFullName]));
            body.AppendLine($"            await {handlerVarName}.{triggerMethod.MethodName}(payload{(dependencies.Any() ? ", " : "")}{diParamNames});");
            body.AppendLine("        }");
            body.AppendLine("        catch (Exception ex)");
            body.AppendLine("        {");
            foreach (var hook in triggerMethod.RequiredHooks.Where(h => h.HookType == "RequiresLogger" && h.LogAction == "OnError"))
            {
                var hookVarName = diMap[hook.HandlerClassFullName];
                body.AppendLine($"            await {hookVarName}.{hook.HandlerMethodName}(ex, context);");
            }
            body.AppendLine("            throw;");
            body.AppendLine("        }");
            return body.ToString();
        }

        private Dictionary<string, string> BuildDependencyMap(TriggerMethod triggerMethod, string handlerVarName)
        {
            var diServices = new Dictionary<string, string>
            {
                { $"ILogger<{triggerMethod.MethodName}_Function>", "_logger" },
                { _blueprint.HandlerClassFullName, handlerVarName }
            };
            triggerMethod.RequiredHooks.ForEach(h => diServices.TryAdd(h.HandlerClassFullName, $"_{h.HandlerMethodName}_hook_{h.HandlerClassFullName.Split('.').Last()}"));
            triggerMethod.Parameters.Where(p => !p.IsPayload).ToList().ForEach(d => diServices.TryAdd(d.TypeFullName, $"_{d.Name}_service"));
            return diServices;
        }

        private string GenerateFileHeader(IEnumerable<string> types)
        {
            return string.Join(Environment.NewLine, types
                .Where(t => t.Contains('.'))
                .Select(t => t.Substring(0, t.LastIndexOf('.')))
                .Append("System.Threading.Tasks")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ns => ns)
                .Select(ns => $"using {ns};"));
        }

        private (string triggerAttribute, string functionSignatureParams, string payloadAcquisition) GenerateTriggerAndPayloadCode(TriggerDefinition trigger, ParameterDefinition payload)
        {
            switch (trigger.Type)
            {
                case "Http":
                    var method = trigger.Properties.GetValueOrDefault("Method", "post")?.ToLowerInvariant();
                    var route = trigger.Name.TrimStart('/');
                    var signature = "HttpRequestData req";
                    var attribute = $"[HttpTrigger(AuthorizationLevel.Anonymous, \"{method}\", Route = \"{route}\")] {signature}";
                    var acquisition = $"            var payload = await req.ReadFromJsonAsync<{payload.TypeFullName}>() ?? throw new ArgumentNullException(nameof(payload));";
                    return (attribute, signature, acquisition);
                case "AzureServiceBusQueue":
                    signature = "string messageBody";
                    attribute = $"[ServiceBusTrigger(\"{trigger.Name}\", Connection = \"ServiceBusConnection\")] {signature}";
                    acquisition = $"            var payload = JsonSerializer.Deserialize<{payload.TypeFullName}>(messageBody) ?? throw new ArgumentNullException(nameof(payload));";
                    return (attribute, signature, acquisition);
                case "Cron":
                    signature = "TimerInfo myTimer";
                    attribute = $"[TimerTrigger(\"{trigger.Name}\")] {signature}";
                    acquisition = $"            var payload = new {payload.TypeFullName}();";
                    return (attribute, signature, acquisition);
                default:
                    throw new NotSupportedException($"Trigger type '{trigger.Type}' is not supported for C# Azure Functions generation.");
            }
        }
    }
}