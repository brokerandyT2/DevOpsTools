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
    /// Implements ILanguageWeaver for generating a Java Azure Functions project using Maven.
    /// </summary>
    public class JavaAzureFunctionsWeaver : ILanguageWeaver
    {
        private readonly IAppLogger _logger;
        private readonly ServiceBlueprint _blueprint;

        public JavaAzureFunctionsWeaver(IAppLogger logger, ServiceBlueprint blueprint)
        {
            _logger = logger;
            _blueprint = blueprint;
        }

        public async Task GenerateProjectFileAsync(string projectPath, string logicSourcePath)
        {
            var dependencies = new StringBuilder();

            // Add required base dependencies for Java on Azure Functions
            dependencies.AppendLine(GetMavenDependency("com.microsoft.azure.functions", "azure-functions-java-library", "3.0.0"));

            // Add dependencies for testing
            dependencies.AppendLine(GetMavenDependency("org.junit.jupiter", "junit-jupiter-api", "5.10.2", "test"));
            dependencies.AppendLine(GetMavenDependency("org.mockito", "mockito-core", "5.11.0", "test"));
            dependencies.AppendLine(GetMavenDependency("org.mockito", "mockito-junit-jupiter", "5.11.0", "test"));

            var pomContent = $@"
<project xmlns=""http://maven.apache.org/POM/4.0.0""
         xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
         xsi:schemaLocation=""http://maven.apache.org/POM/4.0.0 http://maven.apache.org/xsd/maven-4.0.0.xsd"">
    <modelVersion>4.0.0</modelVersion>
    <groupId>com.mycompany.functions</groupId>
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
{dependencies.ToString().TrimEnd()}
        <!-- TODO: Add a dependency for the developer's business logic JAR -->
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
             <plugin>
                <groupId>org.apache.maven.plugins</groupId>
                <artifactId>maven-compiler-plugin</artifactId>
                <version>3.8.1</version>
                <configuration>
                    <source>${{java.version}}</source>
                    <target>${{java.version}}</target>
                </configuration>
            </plugin>
        </plugins>
    </build>
</project>";
            var filePath = Path.Combine(projectPath, "pom.xml");
            await File.WriteAllTextAsync(filePath, pomContent.Trim());
        }

        public Task GenerateStartupFileAsync(string projectPath)
        {
            // Java on Azure Functions uses the main handler class; DI is more complex and
            // typically requires a framework, which is beyond the scope of this basic weaver.
            return Task.CompletedTask;
        }

        public async Task GeneratePlatformFilesAsync(string projectPath)
        {
            var hostJsonContent = @"{
  ""version"": ""2.0"",
  ""extensionBundle"": {
    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
    ""version"": ""[4.*, 5.0.0)""
  }
}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "host.json"), hostJsonContent.Trim());

            var settingsJsonContent = @"{
  ""IsEncrypted"": false,
  ""Values"": {
    ""AzureWebJobsStorage"": ""UseDevelopmentStorage=true"",
    ""FUNCTIONS_WORKER_RUNTIME"": ""java""
  }
}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "local.settings.json"), settingsJsonContent.Trim());
        }

        private string GetMavenDependency(string groupId, string artifactId, string version, string scope = "")
        {
            var scopeXml = string.IsNullOrEmpty(scope) ? "" : $"        <scope>{scope}</scope>";
            return $@"        <dependency>
            <groupId>{groupId}</groupId>
            <artifactId>{artifactId}</artifactId>
            <version>{version}</version>
{scopeXml}
        </dependency>";
        }

        public async Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath)
        {
            var functionClassName = "Function"; // Azure Functions for Java convention
            var packageName = "com.mycompany.functions";
            var packagePath = Path.Combine("src", "main", "java", "com", "mycompany", "functions");
            var fullPath = Path.Combine(projectPath, packagePath);
            Directory.CreateDirectory(fullPath);

            var businessLogicHandlerType = triggerMethod.HandlerClassFullName;
            var handlerVarName = ToCamelCase(businessLogicHandlerType.Split('.').Last());

            var sb = new StringBuilder();
            sb.AppendLine($"package {packageName};");
            sb.AppendLine();

            var imports = new HashSet<string>
            {
                "com.microsoft.azure.functions.*",
                "com.microsoft.azure.functions.annotation.*",
                "java.util.Optional",
                businessLogicHandlerType
            };
            var payload = triggerMethod.Parameters.First(p => p.IsPayload);
            imports.Add(payload.TypeFullName);

            foreach (var import in imports.OrderBy(i => i)) sb.AppendLine($"import {import};");
            sb.AppendLine();
            sb.AppendLine($"// Auto-generated by 3SC DataLink at {DateTime.UtcNow:O}");
            sb.AppendLine($"// Source Version: {_blueprint.Metadata.SourceVersionTag}");
            sb.AppendLine();
            sb.AppendLine($"public class {functionClassName} {{");

            foreach (var method in _blueprint.TriggerMethods)
            {
                var trigger = method.Triggers.First();
                var payloadDto = method.Parameters.First(p => p.IsPayload);
                var payloadTypeName = payloadDto.TypeFullName.Split('.').Last();
                var (triggerAnnotation, triggerParameter) = GetTriggerAnnotation(trigger, payloadTypeName);

                sb.AppendLine();
                sb.AppendLine($"    @FunctionName(\"{method.MethodName}\")");
                sb.AppendLine($"    public HttpResponseMessage {ToCamelCase(method.MethodName)}(");
                sb.AppendLine($"            {triggerAnnotation} {triggerParameter},");
                sb.AppendLine("            final ExecutionContext context) {");
                sb.AppendLine("        context.getLogger().info(\"Java HTTP trigger processed a request.\");");
                sb.AppendLine();
                sb.AppendLine("        // In a real application, a DI framework would provide the handler instance.");
                sb.AppendLine($"        // {businessLogicHandlerType} {handlerVarName} = new {businessLogicHandlerType}();");
                sb.AppendLine();
                sb.AppendLine("        try {");
                sb.AppendLine($"            // Example business logic invocation");
                sb.AppendLine($"            // {handlerVarName}.{ToCamelCase(method.MethodName)}(request.getBody().get());");
                sb.AppendLine("            return request.createResponseBuilder(HttpStatus.OK).body(\"Request processed successfully.\").build();");
                sb.AppendLine("        } catch (Exception e) {");
                sb.AppendLine("            return request.createResponseBuilder(HttpStatus.INTERNAL_SERVER_ERROR).body(\"An error occurred: \" + e.getMessage()).build();");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }
            sb.AppendLine("}");

            await File.WriteAllTextAsync(Path.Combine(fullPath, $"{functionClassName}.java"), sb.ToString());
            _logger.LogDebug($"Generated Java function file: {functionClassName}.java");
        }

        public async Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath)
        {
            _logger.LogInfo("Assembling Java test harness project...");
            var testPackagePath = Path.Combine("src", "test", "java", "com", "mycompany", "functions");
            var fullTestPath = Path.Combine(testProjectPath, testPackagePath);
            Directory.CreateDirectory(fullTestPath);

            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var relevantTestFiles = Directory.GetFiles(testSourcePath, $"*{handlerClassNameShort}Test.java", SearchOption.AllDirectories);
            foreach (var testFile in relevantTestFiles)
            {
                File.Copy(testFile, Path.Combine(fullTestPath, Path.GetFileName(testFile)), true);
            }

            await GenerateSingleTestHarnessFileAsync(fullTestPath);
        }

        private async Task GenerateSingleTestHarnessFileAsync(string testPackagePath)
        {
            var functionClassName = "Function";
            var harnessFileName = "FunctionHarnessTest.java";

            var testMethods = new StringBuilder();
            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                var payloadType = triggerMethod.Parameters.First(p => p.IsPayload).TypeFullName.Split('.').Last();
                testMethods.AppendLine($@"
    @Test
    public void test{triggerMethod.MethodName}_HappyPath() {{
        // This test will fail here until you implement the mock setups and assertions.
        fail(""Test not yet implemented. Please configure mock setups and add assertions, then remove this line."");
        
        /* --- EXAMPLE IMPLEMENTATION ---
        // Arrange
        @SuppressWarnings(""unchecked"")
        final HttpRequestMessage<Optional<{payloadType}>> req = mock(HttpRequestMessage.class);
        final Optional<{payloadType}> payload = Optional.of(new {payloadType}());
        doReturn(payload).when(req).getBody();

        doReturn(new MockHttpResponseMessage.Builder(HttpStatus.OK)).when(req).createResponseBuilder(any(HttpStatus.class));
        
        // Act
        final HttpResponseMessage res = new Function().{ToCamelCase(triggerMethod.MethodName)}(req, context);
        
        // Assert
        assertEquals(HttpStatus.OK, res.getStatus());
        // verify(mockBusinessLogic, times(1)).{ToCamelCase(triggerMethod.MethodName)}(any({payloadType}.class));
        */
    }}");
            }

            var harnessContent = $@"
package com.mycompany.functions;

import com.microsoft.azure.functions.*;
import org.mockito.invocation.InvocationOnMock;
import org.mockito.stubbing.Answer;
import java.util.*;
import java.util.logging.Logger;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.Mockito.*;

// Auto-generated by 3SC DataLink. This test skeleton is designed to fail by default.
public class {functionClassName}HarnessTest {{
    private ExecutionContext context;

    @BeforeEach
    public void setup() {{
        context = mock(ExecutionContext.class);
        doReturn(Logger.getGlobal()).when(context).getLogger();
    }}
{testMethods}

    // Helper class for mocking HttpResponseMessage builder
    public static class MockHttpResponseMessage extends HttpResponseMessage.Builder implements HttpResponseMessage {{
        private HttpStatusType status;
        private Object body;
        public MockHttpResponseMessage(HttpStatusType status, Object body) {{ this.status = status; this.body = body; }}
        public HttpStatusType getStatus() {{ return status; }}
        public Object getBody() {{ return body; }}
        public Builder body(Object body) {{ this.body = body; return this; }}
        public HttpResponseMessage build() {{ return this; }}
    }}
}}";
            await File.WriteAllTextAsync(Path.Combine(testPackagePath, harnessFileName), harnessContent.Trim());
        }

        private (string annotation, string parameter) GetTriggerAnnotation(TriggerDefinition trigger, string payloadTypeName)
        {
            switch (trigger.Type)
            {
                case "Http":
                    var method = trigger.Properties.GetValueOrDefault("Method", "post")?.ToUpperInvariant();
                    var route = trigger.Name.TrimStart('/');
                    var annotation = $"@HttpTrigger(name = \"req\", methods = {{HttpMethod.{method}}}, route = \"{route}\", authLevel = AuthorizationLevel.FUNCTION)";
                    var parameter = $"final HttpRequestMessage<Optional<{payloadTypeName}>> request";
                    return (annotation, parameter);
                case "AzureServiceBusQueue":
                    annotation = $"@ServiceBusQueueTrigger(name = \"msg\", queueName = \"{trigger.Name}\", connection = \"ServiceBusConnection\")";
                    parameter = $"final String message";
                    return (annotation, parameter);
                default:
                    throw new NotSupportedException($"Trigger type '{trigger.Type}' is not supported for Java Azure Functions generation.");
            }
        }

        private string ToCamelCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            return char.ToLowerInvariant(input[0]) + input.Substring(1);
        }
    }
}