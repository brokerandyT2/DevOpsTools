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
    /// Implements ILanguageWeaver for generating a Java AWS Lambda project using Maven.
    /// </summary>
    public class JavaAwsLambdaWeaver : ILanguageWeaver
    {
        private readonly IAppLogger _logger;
        private readonly ServiceBlueprint _blueprint;

        public JavaAwsLambdaWeaver(IAppLogger logger, ServiceBlueprint blueprint)
        {
            _logger = logger;
            _blueprint = blueprint;
        }

        public async Task GenerateProjectFileAsync(string projectPath, string logicSourcePath)
        {
            var uniqueTriggers = _blueprint.TriggerMethods.SelectMany(tm => tm.Triggers).Select(t => t.Type).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var dependencies = new StringBuilder();

            // Add required base dependencies for AWS Lambda
            dependencies.AppendLine(GetMavenDependency("com.amazonaws", "aws-lambda-java-core", "1.2.3"));
            dependencies.AppendLine(GetMavenDependency("com.amazonaws", "aws-lambda-java-events", "3.11.4"));
            dependencies.AppendLine(GetMavenDependency("com.google.code.gson", "gson", "2.10.1")); // For JSON serialization

            // Dynamically add dependencies based on triggers
            if (uniqueTriggers.Contains("Http")) // Assuming Http maps to API Gateway
            {
                // aws-lambda-java-events already contains APIGatewayProxyRequestEvent
            }
            if (uniqueTriggers.Contains("AwsSqsQueue"))
            {
                // aws-lambda-java-events already contains SQSEvent
            }

            var pomContent = $@"
<project xmlns=""http://maven.apache.org/POM/4.0.0"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
    xsi:schemaLocation=""http://maven.apache.org/POM/4.0.0 http://maven.apache.org/maven-v4_0_0.xsd"">
    <modelVersion>4.0.0</modelVersion>
    <groupId>com.mycompany.services</groupId>
    <artifactId>{_blueprint.ServiceName}</artifactId>
    <version>1.0.0</version>
    <name>{_blueprint.ServiceName}</name>
    <properties>
        <maven.compiler.source>17</maven.compiler.source>
        <maven.compiler.target>17</maven.compiler.target>
    </properties>
    <dependencies>
{dependencies.ToString().TrimEnd()}
        <!-- TODO: Add a dependency for the developer's business logic JAR -->
    </dependencies>
    <build>
        <plugins>
            <plugin>
                <groupId>org.apache.maven.plugins</groupId>
                <artifactId>maven-shade-plugin</artifactId>
                <version>3.2.4</version>
                <executions>
                    <execution>
                        <phase>package</phase>
                        <goals>
                            <goal>shade</goal>
                        </goals>
                    </execution>
                </executions>
            </plugin>
        </plugins>
    </build>
</project>";
            var filePath = Path.Combine(projectPath, "pom.xml");
            await File.WriteAllTextAsync(filePath, pomContent.Trim());
            _logger.LogDebug($"Generated pom.xml file: {filePath}");
        }

        public async Task GenerateStartupFileAsync(string projectPath)
        {
            // For AWS Lambda with Java, the "startup" is the Handler class itself.
            // We generate a main handler class for each trigger method.
            // Dependency Injection would be handled by a framework like Dagger or Guice,
            // which would be configured here. For now, we'll generate simple handlers.
            _logger.LogDebug("Java AWS Lambda startup logic is generated within the function files.");
            await Task.CompletedTask;
        }

        public async Task GeneratePlatformFilesAsync(string projectPath)
        {
            // Generate a starter AWS SAM (Serverless Application Model) template.
            var resources = new StringBuilder();
            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                var trigger = triggerMethod.Triggers.First();
                var handlerPath = $"com.mycompany.services.{_blueprint.ServiceName}.{triggerMethod.MethodName}Handler::handleRequest";
                var (eventType, eventProperties) = GetSamEvent(trigger);

                resources.AppendLine($@"  {triggerMethod.MethodName}Function:
    Type: AWS::Serverless::Function
    Properties:
      CodeUri: .
      Handler: {handlerPath}
      Runtime: java17
      Architectures:
        - x86_64
      MemorySize: 512
      Timeout: 100
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
  {_blueprint.ServiceName}

  Auto-generated by 3SC DataLink from source version {_blueprint.Metadata.SourceVersionTag}

Globals:
  Function:
    Timeout: 20

Resources:
{resources.ToString().TrimEnd()}
";
            var filePath = Path.Combine(projectPath, "template.yaml");
            await File.WriteAllTextAsync(filePath, samTemplateContent.Trim());
            _logger.LogDebug($"Generated template.yaml file: {filePath}");
        }

        private string GetMavenDependency(string groupId, string artifactId, string version)
        {
            return $@"        <dependency>
            <groupId>{groupId}</groupId>
            <artifactId>{artifactId}</artifactId>
            <version>{version}</version>
        </dependency>";
        }

        private (string, string) GetSamEvent(TriggerDefinition trigger)
        {
            switch (trigger.Type)
            {
                case "Http":
                    var path = trigger.Name.StartsWith("/") ? trigger.Name : "/" + trigger.Name;
                    var method = trigger.Properties.GetValueOrDefault("Method", "post")?.ToLowerInvariant();
                    var properties = $@"            Path: {path}
            Method: {method}";
                    return ("Api", properties);
                case "AwsSqsQueue":
                    properties = $"            Queue: !GetAtt {trigger.Name}Queue.Arn"; // Assumes a queue is also defined
                    return ("SQS", properties);
                default:
                    return ("Api", "            Path: /default\n            Method: post");
            }
        }
        public async Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath)
        {
            var payload = triggerMethod.Parameters.First(p => p.IsPayload);
            var dependencies = triggerMethod.Parameters.Where(p => !p.IsPayload).ToList();
            var handlerClassName = $"{triggerMethod.MethodName}Handler";
            var packagePath = Path.Combine("src", "main", "java", "com", "mycompany", "services", _blueprint.ServiceName);
            var fullPath = Path.Combine(projectPath, packagePath);
            Directory.CreateDirectory(fullPath);

            var businessLogicHandlerType = _blueprint.HandlerClassFullName;
            var businessLogicHandlerVar = ToCamelCase(businessLogicHandlerType.Split('.').Last());

            var (awsEventRequest, awsEventResponse) = GetAwsEventType(triggerMethod.Triggers.First());

            // Collect all necessary import statements
            var imports = new HashSet<string>
            {
                $"com.amazonaws.services.lambda.runtime.Context",
                $"com.amazonaws.services.lambda.runtime.RequestHandler",
                $"com.amazonaws.services.lambda.runtime.events.{awsEventRequest}",
                $"com.amazonaws.services.lambda.runtime.events.{awsEventResponse}",
                "com.google.gson.Gson",
                "com.google.gson.GsonBuilder",
                _blueprint.HandlerClassFullName // Import the business logic class
            };
            dependencies.ForEach(d => imports.Add(d.TypeFullName));
            triggerMethod.RequiredHooks.ForEach(h => imports.Add(h.HandlerClassFullName));

            var sb = new StringBuilder();
            sb.AppendLine($"package com.mycompany.services.{_blueprint.ServiceName};");
            sb.AppendLine();
            foreach (var import in imports.OrderBy(i => i)) sb.AppendLine($"import {import};");
            sb.AppendLine();
            sb.AppendLine($"// Auto-generated by 3SC DataLink at {DateTime.UtcNow:O}");
            sb.AppendLine($"// Source Version: {_blueprint.Metadata.SourceVersionTag}");
            sb.AppendLine();
            sb.AppendLine($"public class {handlerClassName} implements RequestHandler<{awsEventRequest}, {awsEventResponse}> {{");
            sb.AppendLine();
            sb.AppendLine("    private static final Gson gson = new GsonBuilder().setPrettyPrinting().create();");
            sb.AppendLine($"    private final {businessLogicHandlerType} {businessLogicHandlerVar};");
            // Add fields for hooks and dependencies
            sb.AppendLine();
            sb.AppendLine($"    public {handlerClassName}() {{");
            sb.AppendLine($"        // In a real application, use a DI framework like Dagger or Guice");
            sb.AppendLine($"        this.{businessLogicHandlerVar} = new {businessLogicHandlerType}();");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    @Override");
            sb.AppendLine($"    public {awsEventResponse} handleRequest({awsEventRequest} event, Context context) {{");
            sb.AppendLine("        try {");
            // Generate logic to extract payload from the specific AWS event type
            sb.AppendLine($"            {payload.TypeFullName} payload = gson.fromJson(event.getBody(), {payload.TypeFullName}.class);");
            sb.AppendLine();
            // Weave hook logic and the call to the business logic method here.
            sb.AppendLine($"            this.{businessLogicHandlerVar}.{triggerMethod.MethodName}(payload);");
            sb.AppendLine();
            sb.AppendLine($"            return new {awsEventResponse}().withStatusCode(200).withBody(\"Success\");");
            sb.AppendLine("        } catch (Exception e) {");
            sb.AppendLine("            // Weave OnError logging hooks here");
            sb.AppendLine($"            return new {awsEventResponse}().withStatusCode(500).withBody(\"Internal Server Error\");");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            await File.WriteAllTextAsync(Path.Combine(fullPath, $"{handlerClassName}.java"), sb.ToString());
            _logger.LogDebug($"Generated Java handler file: {handlerClassName}.java");
        }

        public async Task AssembleTestHarnessAsync(string testSourcePath, string testProjectPath, string mainProjectPath)
        {
            _logger.LogInfo("Assembling Java test harness project...");
            var testPackagePath = Path.Combine("src", "test", "java", "com", "mycompany", "services", _blueprint.ServiceName);
            var fullTestPath = Path.Combine(testProjectPath, testPackagePath);
            Directory.CreateDirectory(fullTestPath);

            var testPomContent = $@"
<project xmlns=""http://maven.apache.org/POM/4.0.0"" xmlns:xsi=""http://www.w.org/2001/XMLSchema-instance""
    xsi:schemaLocation=""http://maven.apache.org/POM/4.0.0 http://maven.apache.org/maven-v4_0_0.xsd"">
    <modelVersion>4.0.0</modelVersion>
    <groupId>com.mycompany.services</groupId>
    <artifactId>{_blueprint.ServiceName}-tests</artifactId>
    <version>1.0.0</version>
    <name>{_blueprint.ServiceName} Tests</name>
    <properties>
        <maven.compiler.source>17</maven.compiler.source>
        <maven.compiler.target>17</maven.compiler.target>
    </properties>
    <dependencies>
        <dependency>
            <groupId>org.junit.jupiter</groupId>
            <artifactId>junit-jupiter-api</artifactId>
            <version>5.10.2</version>
            <scope>test</scope>
        </dependency>
        <dependency>
            <groupId>org.mockito</groupId>
            <artifactId>mockito-core</artifactId>
            <version>5.11.0</version>
            <scope>test</scope>
        </dependency>
        <!-- Add dependency to the main project being tested -->
    </dependencies>
</project>";
            await File.WriteAllTextAsync(Path.Combine(testProjectPath, "pom.xml"), testPomContent.Trim());

            var handlerClassNameShort = _blueprint.HandlerClassFullName.Split('.').Last();
            var relevantTestFiles = Directory.GetFiles(testSourcePath, $"*{handlerClassNameShort}Test.java", SearchOption.AllDirectories);
            foreach (var testFile in relevantTestFiles)
            {
                File.Copy(testFile, Path.Combine(fullTestPath, Path.GetFileName(testFile)), true);
                _logger.LogDebug($"Copied developer test file: {Path.GetFileName(testFile)}");
            }

            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                await GenerateSingleTestHarnessFileAsync(triggerMethod, fullTestPath);
            }
        }

        private async Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testPackagePath)
        {
            var handlerClassName = $"{triggerMethod.MethodName}Handler";
            var businessLogicHandlerType = _blueprint.HandlerClassFullName.Split('.').Last();
            var payloadType = triggerMethod.Parameters.First(p => p.IsPayload).TypeFullName.Split('.').Last();

            var harnessContent = $@"
package com.mycompany.services.{_blueprint.ServiceName};

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.MockitoAnnotations;

import static org.junit.jupiter.api.Assertions.fail;
import static org.mockito.Mockito.*;

// Auto-generated by 3SC DataLink. This test skeleton is designed to fail by default.
public class {handlerClassName}HarnessTest {{

    @Mock
    private {businessLogicHandlerType} mockBusinessLogic;

    @InjectMocks
    private {handlerClassName} functionToTest;

    @BeforeEach
    void setUp() {{
        MockitoAnnotations.openMocks(this);
    }}

    @Test
    void {triggerMethod.MethodName}_HappyPath_InvokesBusinessLogic() {{
        // This test will fail here until you implement the mock setups and assertions.
        fail(""Test not yet implemented. Please configure mock setups and assertions, then remove this line."");
        
        /* --- EXAMPLE IMPLEMENTATION ---
        // Arrange
        var event = new com.amazonaws.services.lambda.runtime.events.APIGatewayProxyRequestEvent();
        event.setBody(new com.google.gson.Gson().toJson(new {payloadType}()));
        
        // Act
        // functionToTest.handleRequest(event, null);
        
        // Assert
        // verify(mockBusinessLogic, times(1)).{triggerMethod.MethodName}(any({payloadType}.class));
        */
    }}
}}";
            await File.WriteAllTextAsync(Path.Combine(testPackagePath, $"{handlerClassName}HarnessTest.java"), harnessContent.Trim());
        }

        private (string Request, string Response) GetAwsEventType(TriggerDefinition trigger)
        {
            return trigger.Type switch
            {
                "Http" => ("APIGatewayProxyRequestEvent", "APIGatewayProxyResponseEvent"),
                "AwsSqsQueue" => ("SQSEvent", "Void"), // SQS events don't typically return a value
                _ => ("Object", "Object")
            };
        }

        private string ToCamelCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            return char.ToLowerInvariant(input[0]) + input.Substring(1);
        }
    }
}