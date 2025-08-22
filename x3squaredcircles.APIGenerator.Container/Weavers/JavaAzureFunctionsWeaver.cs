using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;

namespace x3squaredcircles.DataLink.Container.Weavers
{
    public class JavaAzureFunctionsWeaver : JavaWeaverBase
    {
        public JavaAzureFunctionsWeaver(IAppLogger logger, ServiceBlueprint blueprint)
            : base(logger, blueprint) { }

        public override async Task GenerateProjectFileAsync(string projectPath, string logicSourcePath)
        {
            var dependencies = new StringBuilder();
            dependencies.AppendLine(GetMavenDependency("com.microsoft.azure.functions", "azure-functions-java-library", "3.0.0"));

            var businessLogicJarPath = FindBusinessLogicArtifact(logicSourcePath, projectPath);
            var logicDependency = $@"
        <dependency>
            <groupId>{_blueprint.HandlerClassFullName.Substring(0, _blueprint.HandlerClassFullName.LastIndexOf('.'))}</groupId>
            <artifactId>{_blueprint.HandlerClassFullName.Split('.').Last()}</artifactId>
            <version>1.0.0</version>
            <scope>system</scope>
            <systemPath>${{project.basedir}}/{businessLogicJarPath.Replace('\\', '/')}</systemPath>
        </dependency>";

            var testDependencies = new StringBuilder();
            testDependencies.AppendLine(GetMavenDependency("org.junit.jupiter", "junit-jupiter-api", "5.10.2", "test"));
            testDependencies.AppendLine(GetMavenDependency("org.mockito", "mockito-core", "5.11.0", "test"));

            var pomContent = $@"
<project xmlns=""http://maven.apache.org/POM/4.0.0""
         xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
         xsi:schemaLocation=""http://maven.apache.org/POM/4.0.0 http://maven.apache.org/xsd/maven-4.0.0.xsd"">
    <modelVersion>4.0.0</modelVersion>
    <groupId>{_groupId}</groupId>
    <artifactId>{_blueprint.ServiceName}</artifactId>
    <version>1.0.0</version>
    <packaging>jar</packaging>
    <properties>
        <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
        <java.version>17</java.version>
        <azure.functions.maven.plugin.version>1.29.0</azure.functions.maven.plugin.version>
        <azure.functions.runtime.os>linux</azure.functions.runtime.os>
        <functionAppName>{_blueprint.ServiceName.ToLowerInvariant()}-{Guid.NewGuid().ToString().Substring(0, 4)}</functionAppName>
    </properties>
    <dependencies>
{dependencies}
{logicDependency}
{testDependencies}
    </dependencies>
    <build>
        <plugins>
            <plugin>
                <groupId>com.microsoft.azure</groupId>
                <artifactId>azure-functions-maven-plugin</artifactId>
                <version>${{azure.functions.maven.plugin.version}}</version>
                <configuration>
                    <appName>${{functionAppName}}</appName>
                    <resourceGroup>java-functions-group</resourceGroup>
                    <region>eastus</region>
                    <runtime>
                        <os>${{azure.functions.runtime.os}}</os>
                        <javaVersion>${{java.version}}</javaVersion>
                    </runtime>
                    <appSettings>
                        <property>
                            <name>FUNCTIONS_WORKER_RUNTIME</name>
                            <value>java</value>
                        </property>
                    </appSettings>
                </configuration>
                <executions>
                    <execution>
                        <id>package-functions</id>
                        <goals>
                            <goal>package</goal>
                        </goals>
                    </execution>
                </executions>
            </plugin>
        </plugins>
    </build>
</project>";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "pom.xml"), pomContent.Trim());
        }

        public override Task GenerateStartupFileAsync(string projectPath)
        {
            // Java on Azure Functions does not use a single startup file.
            return Task.CompletedTask;
        }

        public override async Task GeneratePlatformFilesAsync(string projectPath)
        {
            var hostJsonContent = @"{""version"": ""2.0"", ""extensionBundle"": {""id"": ""Microsoft.Azure.Functions.ExtensionBundle"", ""version"": ""[4.*, 5.0.0)""}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "host.json"), hostJsonContent.Trim());

            var settingsJsonContent = @"{""IsEncrypted"": false, ""Values"": {""AzureWebJobsStorage"": ""UseDevelopmentStorage=true"", ""FUNCTIONS_WORKER_RUNTIME"": ""java""}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "local.settings.json"), settingsJsonContent.Trim());

            // Generate a function.json for each entry point method.
            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                var functionPath = Path.Combine(projectPath, "src", "main", "resources", triggerMethod.MethodName);
                Directory.CreateDirectory(functionPath);

                var eventSource = triggerMethod.DslAttributes.First(a => a.Name == "EventSource");
                var bindings = ParseUrnForFunctionJson(eventSource.Arguments["EventUrn"]);

                var functionJson = JsonSerializer.Serialize(new { bindings }, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(functionPath, "function.json"), functionJson);
            }
        }

        public override async Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath)
        {
            var packageName = $"{_groupId}.{_blueprint.ServiceName.ToLowerInvariant()}";
            var packagePath = CreatePackagePath(projectPath, "main", packageName);
            var functionClassName = "Function"; // Azure convention is often a single class with multiple annotated methods.
            var functionFilePath = Path.Combine(packagePath, $"{functionClassName}.java");

            var handlerVarName = ToCamelCase(_blueprint.HandlerClassFullName.Split('.').Last());

            var imports = new HashSet<string>
            {
                "com.microsoft.azure.functions.*",
                "com.microsoft.azure.functions.annotation.*",
                "java.util.Optional",
                _blueprint.HandlerClassFullName
            };
            triggerMethod.Parameters.ForEach(p => imports.Add(p.TypeFullName));

            var sb = new StringBuilder();
            if (!File.Exists(functionFilePath))
            {
                // Create the class shell if it's the first time.
                sb.AppendLine($"package {packageName};");
                sb.AppendLine();
                sb.AppendLine(GenerateImports(imports));
                sb.AppendLine();
                sb.AppendLine($"public class {functionClassName} {{");
            }

            var methodParams = string.Join(", ", triggerMethod.Parameters.Select(p => $"final {p.TypeFullName} {p.Name}"));
            var businessLogicCallParams = string.Join(", ", triggerMethod.Parameters.Select(p => p.Name));

            sb.AppendLine($@"
    @FunctionName(""{triggerMethod.MethodName}"")
    public void {ToCamelCase(triggerMethod.MethodName)}({methodParams}, final ExecutionContext context) {{
        context.getLogger().info(""Java shim function '{triggerMethod.MethodName}' is invoking business logic."");
        
        // In a real application, a DI framework would provide the handler instance.
        {_blueprint.HandlerClassFullName} {handlerVarName} = new {_blueprint.HandlerClassFullName}();
        
        {handlerVarName}.{triggerMethod.MethodName}({businessLogicCallParams});
    }}");

            // Append the closing brace later, after all methods have been added.
            File.AppendAllText(functionFilePath, sb.ToString());
        }

        protected override async Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testPackagePath)
        {
            // Placeholder for Azure test harness generation.
            await Task.CompletedTask;
        }

        private List<object> ParseUrnForFunctionJson(string urn)
        {
            var parts = urn.Split(':');
            var bindings = new List<object>();
            if (parts.Length < 4) return bindings; // Invalid URN

            var service = parts[1].ToLowerInvariant();
            var resource = parts[2];
            var action = string.Join(":", parts.Skip(3));

            switch (service)
            {
                case "apigateway":
                    bindings.Add(new
                    {
                        authLevel = "anonymous",
                        type = "httpTrigger",
                        direction = "in",
                        name = "req", // Standard name for the request object
                        methods = new[] { action.ToLowerInvariant() },
                        route = resource
                    });
                    bindings.Add(new
                    {
                        type = "http",
                        direction = "out",
                        name = "$return"
                    });
                    break;
                case "servicebus":
                    bindings.Add(new
                    {
                        type = "serviceBusTrigger",
                        direction = "in",
                        name = "message", // The variable name for the message content
                        queueName = resource,
                        connection = "ServiceBusConnection" // Assumes this app setting exists
                    });
                    break;
            }
            return bindings;
        }
    }
}