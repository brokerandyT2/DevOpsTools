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
    public class GoAzureFunctionsWeaver : GoWeaverBase
    {
        public GoAzureFunctionsWeaver(IAppLogger logger, ServiceBlueprint blueprint)
            : base(logger, blueprint) { }

        public override async Task GenerateProjectFileAsync(string projectPath, string logicSourcePath)
        {
            var moduleName = _blueprint.ServiceName.ToLowerInvariant();
            var goModContent = $@"
module {moduleName}

go 1.21

// It is expected that the developer's go.mod file includes this dependency
// require github.com/dev-container/azure-functions-go-worker v1.3.0
";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "go.mod"), goModContent.Trim());

            var destLogicPath = Path.Combine(projectPath, "business_logic");
            CopyDirectory(logicSourcePath, destLogicPath);
        }

        public override Task GenerateStartupFileAsync(string projectPath)
        {
            // The main.go file serves as the startup/dispatcher for all functions.
            return Task.CompletedTask;
        }

        public override async Task GeneratePlatformFilesAsync(string projectPath)
        {
            var hostJsonContent = @"{""version"": ""2.0"", ""logging"": {""logLevel"": {""default"": ""Information""}}, ""customHandler"": {""description"": {""defaultExecutablePath"": ""handler"", ""workingDirectory"": """", ""arguments"": []}, ""enableForwardingHttpRequest"": true}, ""extensionBundle"": {""id"": ""Microsoft.Azure.Functions.ExtensionBundle"", ""version"": ""[4.*, 5.0.0)""}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "host.json"), hostJsonContent.Trim());

            var settingsJsonContent = @"{""IsEncrypted"": false, ""Values"": {""AzureWebJobsStorage"": ""UseDevelopmentStorage=true"", ""FUNCTIONS_WORKER_RUNTIME"": ""custom""}}";
            await File.WriteAllTextAsync(Path.Combine(projectPath, "local.settings.json"), settingsJsonContent.Trim());

            foreach (var triggerMethod in _blueprint.TriggerMethods)
            {
                var functionPath = Path.Combine(projectPath, triggerMethod.MethodName);
                Directory.CreateDirectory(functionPath);

                var eventSource = triggerMethod.DslAttributes.First(a => a.Name == "EventSource");
                var bindings = ParseUrnForFunctionJson(eventSource.Arguments["EventUrn"]);

                var functionJson = JsonSerializer.Serialize(new { bindings }, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(functionPath, "function.json"), functionJson);
            }
        }

        public override async Task GenerateFunctionFileAsync(TriggerMethod triggerMethod, string projectPath)
        {
            // For Azure Go Custom Handlers, we create a single main.go that acts as a router.
            var mainGoFilePath = Path.Combine(projectPath, "main.go");
            var handlerClassName = _blueprint.HandlerClassFullName.Split('.').Last();

            var sb = new StringBuilder();
            if (!File.Exists(mainGoFilePath))
            {
                sb.AppendLine("package main");
                sb.AppendLine();
                sb.AppendLine("import (");
                sb.AppendLine("    \"encoding/json\"");
                sb.AppendLine("    \"fmt\"");
                sb.AppendLine("    \"io/ioutil\"");
                sb.AppendLine("    \"log\"");
                sb.AppendLine("    \"net/http\"");
                sb.AppendLine("    \"os\"");
                sb.AppendLine($"    logic \"{_blueprint.ServiceName.ToLowerInvariant()}/business_logic\"");
                sb.AppendLine(")");
                sb.AppendLine();
                sb.AppendLine($"var handlerInstance = logic.New{handlerClassName}()");
                sb.AppendLine();
                sb.AppendLine("func main() {");
                sb.AppendLine("    http.HandleFunc(\"/\", defaultHandler)");
            }

            var payloadParam = triggerMethod.Parameters.FirstOrDefault(p => !p.IsBusinessLogicDependency);
            if (payloadParam == null) throw new DataLinkException(ExitCode.CodeGenerationFailed, "GO_PAYLOAD_NOT_FOUND", $"Method '{triggerMethod.MethodName}' requires a payload parameter.");

            // Add a handler for this specific function
            sb.AppendLine($"    http.HandleFunc(\"/{triggerMethod.MethodName}\", {ToCamelCase(triggerMethod.MethodName)}Handler)");

            // Append a closing brace and ListenAndServe later
            File.AppendAllText(mainGoFilePath, sb.ToString());

            // Generate the handler implementation
            var handlerContent = $@"
func {ToCamelCase(triggerMethod.MethodName)}Handler(w http.ResponseWriter, r *http.Request) {{
    var payload logic.{payloadParam.TypeFullName}
    body, _ := ioutil.ReadAll(r.Body)
    json.Unmarshal(body, &payload)
    
    result, err := handlerInstance.{triggerMethod.MethodName}(payload)
    if err != nil {{
        http.Error(w, err.Error(), http.StatusInternalServerError)
        return
    }}

    responseBody, _ := json.Marshal(result)
    fmt.Fprint(w, string(responseBody))
}}";
            await File.AppendAllTextAsync(mainGoFilePath, handlerContent);
        }

        protected override async Task GenerateSingleTestHarnessFileAsync(TriggerMethod triggerMethod, string testPackagePath)
        {
            var harnessFileName = $"{ToSnakeCase(triggerMethod.MethodName)}_harness_test.go";
            await File.WriteAllTextAsync(Path.Combine(testPackagePath, harnessFileName), "// TODO: Implement Go Azure test harness");
        }

        // Finalize the main.go file after all handlers have been added.
        public static async Task FinalizeMainGoFile(string mainGoFilePath)
        {
            if (File.Exists(mainGoFilePath))
            {
                var finalContent = @"
    customHandlerPort, exists := os.LookupEnv(""FUNCTIONS_CUSTOMHANDLER_PORT"")
    if !exists {
        customHandlerPort = ""8080""
    }
    log.Fatal(http.ListenAndServe(fmt.Sprintf("":%s"", customHandlerPort), nil))
}

func defaultHandler(w http.ResponseWriter, r *http.Request) {
    fmt.Fprint(w, ""Default handler: function not found."")
}
";
                await File.AppendAllTextAsync(mainGoFilePath, finalContent);
            }
        }

        private List<object> ParseUrnForFunctionJson(string urn)
        {
            var parts = urn.Split(':');
            var bindings = new List<object>();
            if (parts.Length < 4) return bindings;

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
                        name = "req",
                        methods = new[] { action.ToLowerInvariant() },
                        route = resource.TrimStart('/')
                    });
                    bindings.Add(new { type = "http", direction = "out", name = "res" });
                    break;
            }
            return bindings;
        }
    }
}