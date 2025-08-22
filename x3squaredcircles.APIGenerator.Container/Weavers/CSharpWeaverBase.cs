using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;
using x3squaredcircles.datalink.container.Weavers;

namespace x3squaredcircles.DataLink.Container.Weavers
{
    /// <summary>
    /// Provides a base implementation for C# language weavers, containing common logic for
    // test harness assembly, dependency mapping, and source file header generation.
    /// </summary>
    public abstract class CSharpWeaverBase : ILanguageWeaver
    {
        protected readonly IAppLogger _logger;
        protected readonly ServiceBlueprint _blueprint;

        protected CSharpWeaverBase(IAppLogger logger, ServiceBlueprint blueprint)
        {
            _logger = logger;
            _blueprint = blueprint;
        }

        #region Abstract Methods (To be implemented by concrete weavers)

        public abstract Task GenerateProjectFileAsync(string projectPath, string logicSourcePath);
        public abstract Task GenerateStartupFileAsync(string projectPath);
        public abstract Task GeneratePlatformFilesAsync(string projectPath);
        public abstract Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath);
        protected abstract Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testProjectPath);

        #endregion

        #region Concrete Shared Logic

        /// <summary>
        /// Assembles a complete, buildable test harness project. This shared implementation
        /// creates the test project file, copies the developer's business logic tests, and
        /// calls the abstract method to generate the shim-specific test harness files.
        /// </summary>
        public async Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath)
        {
            _logger.LogInfo($"Assembling C# test harness project for {_blueprint.ServiceName}...");
            var testProjectName = $"{_blueprint.ServiceName}.Tests";

            var relativeMainPath = Path.GetRelativePath(testProjectPath, mainProjectPath);
            var logicProjectFilePath = Directory.GetFiles(testSourcePath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            if (logicProjectFilePath == null)
            {
                // If developer tests don't exist, we can't reference them, but we can still generate the harness.
                _logger.LogWarning("Could not find a .csproj file in the test harness source path. The generated test harness will not include a reference to developer-provided tests.");
            }

            var logicProjectRef = logicProjectFilePath != null
                ? $@"<ProjectReference Include=""..\{Path.GetRelativePath(testProjectPath, logicProjectFilePath)}"" />"
                : "<!-- No developer test project found to reference -->";

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
    {logicProjectRef}
  </ItemGroup>
</Project>";
            await File.WriteAllTextAsync(Path.Combine(testProjectPath, $"{testProjectName}.csproj"), testCsprojContent.Trim());

            if (logicProjectFilePath != null)
            {
                var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
                var relevantTestFiles = Directory.GetFiles(Path.GetDirectoryName(logicProjectFilePath)!, $"*{handlerClassNameShort}Tests.cs", SearchOption.AllDirectories);
                foreach (var testFile in relevantTestFiles)
                {
                    var destinationFile = Path.Combine(testProjectPath, Path.GetFileName(testFile));
                    File.Copy(testFile, destinationFile, true);
                    _logger.LogDebug($"Copied developer business logic test file: {Path.GetFileName(testFile)}");
                }
            }

            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                await GenerateSingleTestHarnessFileAsync(triggerMethod, testProjectPath);
            }
        }

        #endregion

        #region Protected Helper Methods

        /// <summary>
        /// Generates a sorted and distinct list of 'using' statements for a C# file.
        /// </summary>
        protected string GenerateFileHeader(IEnumerable<string> types)
        {
            return string.Join(Environment.NewLine, types
                .Where(t => t != null && t.Contains('.'))
                .Select(t => t.Substring(0, t.LastIndexOf('.')))
                .Append("System.Threading.Tasks")
                .Append("Microsoft.Extensions.Logging")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ns => ns)
                .Select(ns => $"using {ns};"));
        }

        /// <summary>
        /// Converts a PascalCase string to camelCase.
        /// </summary>
        protected string ToCamelCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            return char.ToLowerInvariant(input[0]) + input.Substring(1);
        }

        /// <summary>
        /// Discovers all unique services that need to be registered in the DI container.
        /// </summary>
        protected IEnumerable<string> GetAllRequiredServices()
        {
            var services = new HashSet<string> { _blueprint.HandlerClassFullName };
            foreach (var method in _blueprint.TriggerMethods)
            {
                foreach (var dslAttr in method.DslAttributes.Where(a => a.Name is "Requires" or "RequiresLogger"))
                {
                    if (dslAttr.Arguments.TryGetValue("Handler", out var handlerType))
                    {
                        services.Add(handlerType);
                    }
                }
                foreach (var param in method.Parameters.Where(p => p.IsBusinessLogicDependency))
                {
                    services.Add(param.TypeFullName);
                }
            }
            return services;
        }

        #endregion
    }
}