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
    /// Implements ILanguageWeaver for generating a C# Azure Functions project.
    /// </summary>
    public class CSharpWeaver : ILanguageWeaver
    {
        private readonly IAppLogger _logger;
        private readonly ServiceBlueprint _blueprint;

        public CSharpWeaver(IAppLogger logger, ServiceBlueprint blueprint)
        {
            _logger = logger;
            _blueprint = blueprint;
        }

        public async Task GenerateProjectFileAsync(string projectPath, string logicSourcePath)
        {
            var uniqueTriggers = _blueprint.TriggerMethods.SelectMany(tm => tm.Triggers).Select(t => t.Type).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var packageReferences = new StringBuilder();

            packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker"" Version=""1.21.0"" />");
            packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker.Sdk"" Version=""1.17.0"" />");
            packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.ApplicationInsights.WorkerService"" Version=""2.22.0"" />");

            if (uniqueTriggers.Contains("Http")) packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker.Extensions.Http"" Version=""3.1.0"" />");
            if (uniqueTriggers.Contains("AzureServiceBusQueue")) packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker.Extensions.ServiceBus"" Version=""5.15.0"" />");
            if (uniqueTriggers.Contains("Cron")) packageReferences.AppendLine(@"    <PackageReference Include=""Microsoft.Azure.Functions.Worker.Extensions.Timer"" Version=""4.3.0"" />");

            var logicProjectFilePath = Directory.GetFiles(logicSourcePath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            if (logicProjectFilePath == null) throw new DataLinkException(ExitCode.SourceAnalysisFailed, "CSPROJ_NOT_FOUND", "Could not find a .csproj file in the business logic source path.");
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
            await File.WriteAllTextAsync(Path.Combine(projectPath, $"{_blueprint.ServiceName}.csproj"), csprojContent.Trim());
        }

        public async Task GenerateStartupFileAsync(string projectPath)
        {
            var allHandlerClasses = _blueprint.TriggerMethods
                .SelectMany(tm => tm.RequiredHooks.Select(h => h.HandlerClassFullName))
                .Append(_blueprint.HandlerClassFullName)
                .Distinct();
            var diRegistrations = string.Join(Environment.NewLine, allHandlerClasses.Select(cls => $"        services.AddTransient<{cls}>();"));

            var allDependencyInterfaces = _blueprint.TriggerMethods
                .SelectMany(tm => tm.Parameters.Where(p => !p.IsPayload && p.TypeFullName.StartsWith("I")))
                .Select(p => p.TypeFullName)
                .Distinct();
            var dependencyComment = allDependencyInterfaces.Any() ? "// NOTE: Register the concrete implementations for your business logic's dependencies below." : "// No external dependencies were detected in trigger method signatures.";
            var dependencyRegistrations = string.Join(Environment.NewLine, allDependencyInterfaces.Select(idep => $"        // services.AddSingleton<{idep}, Concrete{idep.Split('.').Last().Substring(1)}>();"));

            var usingStatements = allHandlerClasses.Concat(allDependencyInterfaces)
                .Where(cls => cls.Contains('.'))
                .Select(cls => cls.Substring(0, cls.LastIndexOf('.')))
                .Distinct()
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
        }

        public async Task GeneratePlatformFilesAsync(string projectPath)
        {
            var hostJsonContent = @"{""version"": ""2.0"",""logging"": {""applicationInsights"": {""samplingSettings"": {""isEnabled"": true,""excludedTypes"": ""Request""}}},""extensionBundle"": {""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",""version"": ""[4.*, 5.0.0)""}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "host.json"), hostJsonContent);

            var settingsJsonContent = @"{""IsEncrypted"": false,""Values"": {""AzureWebJobsStorage"": ""UseDevelopmentStorage=true"",""FUNCTIONS_WORKER_RUNTIME"": ""dotnet-isolated""}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "local.settings.json"), settingsJsonContent);
        }

        public async Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath)
        {
            var payload = triggerMethod.Parameters.First(p => p.IsPayload);
            var dependencies = triggerMethod.Parameters.Where(p => !p.IsPayload).ToList();
            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var handlerVarName = "_" + char.ToLowerInvariant(handlerClassNameShort[0]) + handlerClassNameShort.Substring(1);

            var diMap = BuildDependencyMap(triggerMethod, handlerVarName);
            var ctorParams = string.Join(", ", diMap.Select(kvp => $"{kvp.Key} {kvp.Value.TrimStart('_')}"));
            var ctorAssignments = string.Join(Environment.NewLine, diMap.Select(kvp => $"        {kvp.Value} = {kvp.Value.TrimStart('_')};"));

            var (triggerAttribute, payloadAcquisitionLogic) = GenerateTriggerAndPayloadCode(triggerMethod.Triggers.First(), payload);

            var sb = new StringBuilder();
            sb.AppendLine(GenerateFileHeader(diMap.Keys.Append(payload.TypeFullName)));
            sb.AppendLine($"namespace {_blueprint.ServiceName};");
            sb.AppendLine();
            sb.AppendLine($"public class {triggerMethod.MethodName}_Function");
            sb.AppendLine("{");
            foreach (var service in diMap) sb.AppendLine($"    private readonly {service.Key} {service.Value};");
            sb.AppendLine();
            sb.AppendLine($"    public {triggerMethod.MethodName}_Function({ctorParams})");
            sb.AppendLine("    {");
            sb.AppendLine(ctorAssignments);
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    [Function(\"{triggerMethod.MethodName}\")]");
            sb.AppendLine($"    public async Task Run({triggerAttribute})");
            sb.AppendLine("    {");
            sb.AppendLine(await GenerateMethodBodyAsync(triggerMethod, payload, dependencies, handlerVarName, diMap));
            sb.AppendLine("    }");
            sb.AppendLine("}");

            await File.WriteAllTextAsync(Path.Combine(projectPath, $"{triggerMethod.MethodName}_Function.cs"), sb.ToString());
        }

        public async Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath)
        {
            var testProjectName = $"{_blueprint.ServiceName}.Tests";
            var logicProjectFilePath = Directory.GetFiles(logicSourcePath, "*.csproj", SearchOption.AllDirectories).First();
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
    <ProjectReference Include=""{relativeMainPath}\{_blueprint.ServiceName}.csproj"" />
    <ProjectReference Include=""{relativeLogicPath}"" />
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

        // Private helper methods for C# generation
        private async Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testProjectPath)
        {
            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var handlerVarName = "_" + char.ToLowerInvariant(handlerClassNameShort[0]) + handlerClassNameShort.Substring(1);
            var functionClassName = $"{triggerMethod.MethodName}_Function";
            var payload = triggerMethod.Parameters.First(p => p.IsPayload);
            var diMap = BuildDependencyMap(triggerMethod, handlerVarName);

            var mockFields = string.Join(Environment.NewLine, diMap.Select(kvp => $"    private readonly Mock<{kvp.Key}> _mock{kvp.Key.Split('.').Last()};"));
            var mockInits = string.Join(Environment.NewLine, diMap.Select(kvp => $"        _mock{kvp.Key.Split('.').Last()} = new Mock<{kvp.Key}>();"));
            var mockObjects = string.Join(", ", diMap.Select(kvp => $"_mock{kvp.Key.Split('.').Last()}.Object"));

            var harnessContent = $@"
using Xunit;
using Moq;
using System;
using Microsoft.Extensions.Logging;
{GenerateFileHeader(diMap.Keys.Append(payload.TypeFullName)).Replace("using System.Threading.Tasks;", "")}
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
        var payload = new {payload.TypeFullName}();
        
        // This test will fail until implemented.
        throw new NotImplementedException(""Test not yet implemented. Please configure mock setups and assertions."");
        
        /* --- EXAMPLE IMPLEMENTATION ---
        // Act
        // function.Run(...); // Requires mocking the trigger input
        
        // Assert
        // _mock{handlerClassNameShort}.Verify(h => h.{triggerMethod.MethodName}(It.IsAny<{payload.TypeFullName}>(), ...), Times.Once);
        */
    }}
}}";
            await File.WriteAllTextAsync(Path.Combine(testProjectPath, $"{functionClassName}_Harness.cs"), harnessContent.Trim());
        }

        private async Task<string> GenerateMethodBodyAsync(TriggerMethod triggerMethod, ParameterDefinition payload, List<ParameterDefinition> dependencies, string handlerVarName, Dictionary<string, string> diMap)
        {
            var body = new StringBuilder();
            body.AppendLine($"        var context = new TriggerContext(); // This would be populated with trigger-specific metadata");
            body.AppendLine("        try {");
            foreach (var hook in triggerMethod.RequiredHooks.Where(h => h.HookType.StartsWith("Requires") && !h.HookType.Contains("Logger")))
            {
                body.AppendLine($"            var authResult = {diMap[hook.HandlerClassFullName]}.{hook.HandlerMethodName}(req); // Note: Simplified for HttpTrigger");
                body.AppendLine("            if (!authResult) { /* return 401 Unauthorized response */ return; }");
            }
            foreach (var hook in triggerMethod.RequiredHooks.Where(h => h.HookType == "RequiresLogger" && h.LogAction == "OnInbound"))
            {
                body.AppendLine($"            await {diMap[hook.HandlerClassFullName]}.{hook.HandlerMethodName}(payload, context);");
            }
            var diParamNames = string.Join(", ", dependencies.Select(d => diMap[d.TypeFullName]));
            body.AppendLine($"            await {handlerVarName}.{triggerMethod.MethodName}(payload{(dependencies.Any() ? ", " : "")}{diParamNames});");
            body.AppendLine("        }");
            body.AppendLine("        catch (Exception ex)");
            body.AppendLine("        {");
            foreach (var hook in triggerMethod.RequiredHooks.Where(h => h.HookType == "RequiresLogger" && h.LogAction == "OnError"))
            {
                body.AppendLine($"            await {diMap[hook.HandlerClassFullName]}.{hook.HandlerMethodName}(ex, context);");
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

        private (string triggerAttribute, string payloadAcquisition) GenerateTriggerAndPayloadCode(TriggerDefinition trigger, ParameterDefinition payload)
        {
            switch (trigger.Type)
            {
                case "Http":
                    var method = trigger.Properties.GetValueOrDefault("Method", "post")?.ToLowerInvariant();
                    var route = trigger.Name.TrimStart('/');
                    return ($"[HttpTrigger(AuthorizationLevel.Anonymous, \"{method}\", Route = \"{route}\")] HttpRequestData req",
                            $"var payload = await req.ReadFromJsonAsync<{payload.TypeFullName}>() ?? throw new ArgumentNullException(nameof(payload));");
                case "AzureServiceBusQueue":
                    return ($"[ServiceBusTrigger(\"{trigger.Name}\", Connection = \"ServiceBusConnection\")] string messageBody",
                            $"var payload = JsonSerializer.Deserialize<{payload.TypeFullName}>(messageBody) ?? throw new ArgumentNullException(nameof(payload));");
                case "Cron":
                    return ($"[TimerTrigger(\"{trigger.Name}\")] TimerInfo myTimer",
                            $"var payload = new {payload.TypeFullName}(); // Cron jobs typically do not have a direct payload.");
                default:
                    throw new NotSupportedException($"Trigger type '{trigger.Type}' is not supported for C# Azure Functions generation.");
            }
        }
    }
}