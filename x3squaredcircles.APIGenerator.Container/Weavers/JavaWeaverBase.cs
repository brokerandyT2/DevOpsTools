using System.IO;
using System.Linq;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;
using x3squaredcircles.datalink.container.Weavers;

namespace x3squaredcircles.DataLink.Container.Weavers
{
    public abstract class JavaWeaverBase : ILanguageWeaver
    {
        protected readonly IAppLogger _logger;
        protected readonly ServiceBlueprint _blueprint;
        protected readonly string _groupId = "com.threese";

        protected JavaWeaverBase(IAppLogger logger, ServiceBlueprint blueprint)
        {
            _logger = logger;
            _blueprint = blueprint;
        }

        public abstract Task GenerateProjectFileAsync(string projectPath, string logicSourcePath);
        public abstract Task GenerateStartupFileAsync(string projectPath);
        public abstract Task GeneratePlatformFilesAsync(string projectPath);
        public abstract Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath);
        protected abstract Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testPackagePath);

        public async Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath)
        {
            _logger.LogInfo($"Assembling Java test harness project for {_blueprint.ServiceName}...");
            var testPackageName = $"{_groupId}.{_blueprint.ServiceName.ToLowerInvariant()}";
            var testPackagePath = CreatePackagePath(testProjectPath, "test", testPackageName);

            var testPomContent = $@"
<project xmlns=""http://maven.apache.org/POM/4.0.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
    xsi:schemaLocation=""http://maven.apache.org/POM/4.0.0 http://maven.apache.org/maven-v4_0_0.xsd"">
    <modelVersion>4.0.0</modelVersion>
    <groupId>{_groupId}</groupId>
    <artifactId>{_blueprint.ServiceName}-tests</artifactId>
    <version>1.0.0</version>
    <name>{_blueprint.ServiceName} Tests</name>
    <properties>
        <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
        <maven.compiler.source>17</maven.compiler.source>
        <maven.compiler.target>17</maven.compiler.target>
        <junit.version>5.10.2</junit.version>
        <mockito.version>5.11.0</mockito.version>
    </properties>
    <dependencies>
        {GetMavenDependency("org.junit.jupiter", "junit-jupiter-api", "${junit.version}", "test")}
        {GetMavenDependency("org.mockito", "mockito-core", "${mockito.version}", "test")}
        {GetMavenDependency("org.mockito", "mockito-junit-jupiter", "${mockito.version}", "test")}
        <dependency>
            <groupId>{_groupId}</groupId>
            <artifactId>{_blueprint.ServiceName}</artifactId>
            <version>1.0.0</version>
            <scope>test</scope>
        </dependency>
    </dependencies>
</project>";
            await File.WriteAllTextAsync(Path.Combine(testProjectPath, "pom.xml"), testPomContent.Trim());

            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var relevantTestFiles = Directory.GetFiles(testSourcePath, $"*{handlerClassNameShort}Test.java", SearchOption.AllDirectories);
            foreach (var testFile in relevantTestFiles)
            {
                File.Copy(testFile, Path.Combine(testPackagePath, Path.GetFileName(testFile)), true);
                _logger.LogDebug($"Copied developer business logic test file: {Path.GetFileName(testFile)}");
            }

            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                await GenerateSingleTestHarnessFileAsync(triggerMethod, testPackagePath);
            }
        }

        protected string GetMavenDependency(string groupId, string artifactId, string version, string scope = "")
        {
            var scopeXml = string.IsNullOrEmpty(scope) ? "" : $"<scope>{scope}</scope>";
            return $@"<dependency>
            <groupId>{groupId}</groupId>
            <artifactId>{artifactId}</artifactId>
            <version>{version}</version>
            {scopeXml}
        </dependency>";
        }

        protected string ToCamelCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            return char.ToLowerInvariant(input[0]) + input.Substring(1);
        }

        protected string GenerateImports(System.Collections.Generic.IEnumerable<string> types)
        {
            return string.Join(System.Environment.NewLine, types
                .Where(t => t != null && t.Contains('.'))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(ns => ns)
                .Select(ns => $"import {ns};"));
        }

        protected string CreatePackagePath(string projectPath, string subfolder, string packageName)
        {
            var packageDirectory = packageName.Replace('.', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(projectPath, "src", subfolder, "java", packageDirectory);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        protected string FindBusinessLogicArtifact(string logicSourcePath, string projectPath)
        {
            // A common pattern is for the compiled JAR to be in a 'target' subdirectory.
            var targetDir = new DirectoryInfo(Path.Combine(logicSourcePath, "target"));
            if (!targetDir.Exists)
            {
                throw new DataLinkException(ExitCode.BuildFailed, "JAVA_ARTIFACT_NOT_FOUND", $"Could not find 'target' directory in the provided business logic path: {logicSourcePath}. Please ensure the logic has been built with 'mvn package'.");
            }

            // Find the main JAR, excluding 'sources' or 'javadoc' jars.
            var artifact = targetDir.GetFiles("*.jar")
                .FirstOrDefault(f => !f.Name.EndsWith("-sources.jar") && !f.Name.EndsWith("-javadoc.jar"));

            if (artifact == null)
            {
                throw new DataLinkException(ExitCode.BuildFailed, "JAVA_ARTIFACT_NOT_FOUND", $"Could not find a compiled .jar artifact in '{targetDir.FullName}'.");
            }

            // Return a relative path from the generated project's perspective for use in the pom.xml
            return Path.GetRelativePath(projectPath, artifact.FullName);
        }
    }
}