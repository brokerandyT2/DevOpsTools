using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;

namespace x3squaredcircles.DataLink.Container.Weavers
{
    public class CSharpAzureFunctionsWeaver : CSharpWeaverBase
    {
        public CSharpAzureFunctionsWeaver(IAppLogger logger, ServiceBlueprint blueprint)
            : base(logger, blueprint) { }

        public override async Task GenerateProjectFileAsync(string projectPath, string logicSourcePath)
        {
            // Discover all unique NuGet packages required by the trigger attributes found in the source.
            // This is a placeholder for a more sophisticated package discovery mechanism.
            var requiredPackages = new HashSet<string>
            {
                "Microsoft.Azure.Functions.Worker.Extensions.Http",
                "Microsoft.Azure.Functions.Worker.Extensions.ServiceBus",
                "Microsoft.Azure.Functions.Worker.Extensions.Timer"
            };

            var packageReferences = string.Join("\n", requiredPackages.Select(p =>
                $@"    <PackageReference Include=""{p}"" Version=""{GetPackageVersion(p)}"" />"));

            var logicProjectFilePath = Directory.GetFiles(logicSourcePath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            if (logicProjectFilePath == null) throw new DataLinkException(ExitCode.SourceAnalysisFailed, "LOGIC_CSPROJ_NOT_FOUND", "Could not find a .csproj file in the business logic source path.");
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
    <PackageReference Include=""Microsoft.Azure.Functions.Worker"" Version=""1.21.0"" />
    <PackageReference Include=""Microsoft.Azure.Functions.Worker.Sdk"" Version=""1.17.0"" />
    <PackageReference Include=""Microsoft.ApplicationInsights.WorkerService"" Version=""2.22.0"" />
{packageReferences}
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

        public override async Task GenerateStartupFileAsync(string projectPath)
        {
            var allRequiredServices = GetAllRequiredServices();
            var allFunctionClasses = _blueprint.TriggerMethods.Select(tm => $"{tm.MethodName}_Function").Distinct();

            var diRegistrations = string.Join(Environment.NewLine, allRequiredServices.Select(cls => $"        services.AddTransient<{cls}>();"));
            var functionRegistrations = string.Join(Environment.NewLine, allFunctionClasses.Select(cls => $"        services.AddTransient<{cls}>();"));

            var usingStatements = GenerateFileHeader(allRequiredServices);

            var programContent = $@"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
{usingStatements}
using {_blueprint.ServiceName}; // Add the root namespace for the generated functions

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {{
        // Register developer-defined handler, hook, and dependency classes
{diRegistrations}

        // Register the generated function classes themselves
{functionRegistrations}
    }})
    .Build();

host.Run();";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "Program.cs"), programContent.Trim());
        }

        public override async Task GeneratePlatformFilesAsync(string projectPath)
        {
            var hostJsonContent = @"{""version"": ""2.0"",""logging"": {""applicationInsights"": {""samplingSettings"": {""isEnabled"": true,""excludedTypes"": ""Request""}}},""extensionBundle"": {""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",""version"": ""[4.*, 5.0.0)""}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "host.json"), hostJsonContent);

            var settingsJsonContent = @"{""IsEncrypted"": false,""Values"": {""AzureWebJobsStorage"": ""UseDevelopmentStorage=true"",""FUNCTIONS_WORKER_RUNTIME"": ""dotnet-isolated""}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "local.settings.json"), settingsJsonContent);
        }

        public override async Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath)
        {
            var functionClassName = $"{triggerMethod.MethodName}_Function";
            var handlerVarName = $"_{ToCamelCase(_blueprint.HandlerClassFullName.Split('.').Last())}";

            // Build the full list of dependencies to be injected into the constructor.
            var diServices = new Dictionary<string, string> { { _blueprint.HandlerClassFullName, handlerVarName } };
            foreach (var service in GetAllRequiredServices().Where(s => s != _blueprint.HandlerClassFullName))
            {
                diServices.TryAdd(service, $"_{ToCamelCase(service.Split('.').Last())}");
            }
            diServices.TryAdd($"ILogger<{functionClassName}>", "_logger");

            var ctorParams = string.Join(", ", diServices.Select(kvp => $"{kvp.Key} {kvp.Value.TrimStart('_')}"));
            var ctorAssignments = string.Join(Environment.NewLine, diServices.Select(kvp => $"        {kvp.Value} = {kvp.Value.TrimStart('_')};"));

            // Clone attributes and parameters directly from the developer's signature.
            var methodAttributes = string.Join(Environment.NewLine, triggerMethod.Attributes.Select(a => $"    {a.FullSyntax}"));
            var methodParams = string.Join(", ", triggerMethod.Parameters.Select(p => $"{string.Join(" ", p.Attributes.Select(a => a.FullSyntax))} {p.TypeFullName} {p.Name}"));
            var businessLogicCallParams = string.Join(", ", triggerMethod.Parameters.Select(p => p.IsBusinessLogicDependency ? diServices[p.TypeFullName] : p.Name));

            var allUsingTypes = triggerMethod.Parameters.Select(p => p.TypeFullName).Concat(diServices.Keys);

            var functionContent = $@"
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
{GenerateFileHeader(allUsingTypes)}

namespace {_blueprint.ServiceName}
{{
    public class {functionClassName}
    {{
{string.Join(Environment.NewLine, diServices.Select(kvp => $"        private readonly {kvp.Key} {kvp.Value};"))}

        public {functionClassName}({ctorParams})
        {{
{ctorAssignments}
        }}

{methodAttributes}
        public async Task<{triggerMethod.ReturnType}> Run({methodParams})
        {{
            _logger.LogInformation(""Shim function '{functionClassName}' is invoking the business logic."");
            return await {handlerVarName}.{triggerMethod.MethodName}({businessLogicCallParams});
        }}
    }}
}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, $"{functionClassName}.cs"), functionContent.Trim());
        }

        protected override async Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testProjectPath)
        {
            var functionClassName = $"{triggerMethod.MethodName}_Function";
            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();

            var diServices = new Dictionary<string, string>();
            // Populate with mocks...

            var harnessContent = $@"
using Xunit;
using Moq;
using System.Threading.Tasks;
// ... more using statements

namespace {_blueprint.ServiceName}.Tests
{{
    // NOTE: Test harness generation for the signature-driven model is complex
    // and requires mocking framework-specific types like HttpRequestData.
    // This is a simplified placeholder.
    public class {functionClassName}_HarnessTests
    {{
        [Fact]
        public void Test_Placeholder()
        {{
            // TODO: Implement harness test
            Assert.True(true);
        }}
    }}
}}";
            await File.WriteAllTextAsync(Path.Combine(testProjectPath, $"{functionClassName}_Harness.cs"), harnessContent.Trim());
        }

        private string GetPackageVersion(string packageName)
        {
            return packageName switch
            {
                "Microsoft.Azure.Functions.Worker.Extensions.Http" => "3.1.0",
                "Microsoft.Azure.Functions.Worker.Extensions.ServiceBus" => "5.15.0",
                "Microsoft.Azure.Functions.Worker.Extensions.Timer" => "4.3.0",
                _ => "1.0.0"
            };
        }
    }
}