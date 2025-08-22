using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;
using x3squaredcircles.datalink.container.Services;
using x3squaredcircles.datalink.container.Weavers;

namespace x3squaredcircles.datalink.container
{
    public static class Program
    {
        private static readonly string ToolName = "3sc-datalink-assembler";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "6.0.0";

        public static async Task<int> Main(string[] args)
        {
            WritePipelineToolsLogAsync().ConfigureAwait(false).GetAwaiter().GetResult();    
            var configService = new ConfigurationService();
            DataLinkConfiguration config;
            try { config = configService.GetConfiguration(); }
            catch (DataLinkException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"FATAL CONFIGURATION ERROR [{ex.ErrorCode}]: {ex.Message}");
                Console.ResetColor();
                return (int)ex.ExitCode;
            }

            if (config.ListVariablesAndExit)
            {
                return await ExecuteVariableDiscoveryMode(config);
            }

            return await RunCommandLineApplication(args, config);
        }

        private static async Task<int> ExecuteVariableDiscoveryMode(DataLinkConfiguration config)
        {
            var host = BuildHost(config);
            var logger = host.Services.GetRequiredService<IAppLogger>();
            logger.LogInfo($"🚀 {ToolName} v{ToolVersion} starting in variable discovery mode...");
            var orchestrator = host.Services.GetRequiredService<IDataLinkOrchestrator>();

            try
            {
                var variables = await orchestrator.DiscoverRequiredVariablesAsync();
                if (!variables.Any())
                {
                    logger.LogInfo("✅ No placeholders found in [EventSource] attributes. No custom variables are required.");
                }
                else
                {
                    logger.LogInfo("--- Discovered Required Placeholder Variables ---");
                    foreach (var variable in variables.OrderBy(v => v))
                    {
                        logger.LogInfo($"  - {variable} (Expected env var: DATALINK_CUSTOM_{variable.ToUpperInvariant()})");
                    }
                    logger.LogInfo("-----------------------------------------------");
                    logger.LogWarning("These placeholders must be mapped to environment variables in the CI/CD pipeline.");
                }
                logger.LogInfo($"Exiting with code {(int)ExitCode.VariablesDiscovered} as per business rule.");
                return (int)ExitCode.VariablesDiscovered;
            }
            catch (Exception ex)
            {
                HandleFatalException(logger, ex, "variable discovery");
                return ex is DataLinkException de ? (int)de.ExitCode : (int)ExitCode.UnhandledException;
            }
        }

        private static async Task<int> RunCommandLineApplication(string[] args, DataLinkConfiguration config)
        {
            var discoverVarsCommand = new Command("discover-vars", "Scans source code and prints a JSON list of all required placeholder variables.");
            var generateCommand = new Command("generate", "Generates the API shim source code from business logic.");

            var buildCommand = new Command("build", "Builds and packages a previously generated API shim.");
            var groupOption = new Option<string>("--group", "The name of the deployment group to build/deploy.") { IsRequired = true };
            buildCommand.AddOption(groupOption);

            var deployCommand = new Command("deploy", "Deploys a previously built API shim artifact.");
            var artifactPathOption = new Option<FileInfo>("--artifact-path", "The path to the artifact to be deployed.") { IsRequired = true };
            deployCommand.AddOption(groupOption);
            deployCommand.AddOption(artifactPathOption);

            var rootCommand = new RootCommand($"3SC DataLink Assembler v{ToolVersion}")
            {
                discoverVarsCommand,
                generateCommand,
                buildCommand,
                deployCommand
            };

            var host = BuildHost(config);
            var logger = host.Services.GetRequiredService<IAppLogger>();
            var orchestrator = host.Services.GetRequiredService<IDataLinkOrchestrator>();

            // CORRECTED: SetHandler delegates are defined correctly.
            discoverVarsCommand.SetHandler(async (invocation) =>
            {
                var variables = await orchestrator.DiscoverRequiredVariablesAsync();
                var json = JsonSerializer.Serialize(variables.OrderBy(v => v), new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                invocation.ExitCode = (int)ExitCode.Success;
            });

            generateCommand.SetHandler(async (invocation) => { invocation.ExitCode = await orchestrator.GenerateAsync(); });

            buildCommand.SetHandler(async (invocation) =>
            {
                var group = invocation.ParseResult.GetValueForOption(groupOption);
                invocation.ExitCode = await orchestrator.BuildAsync(group!);
            });

            deployCommand.SetHandler(async (invocation) =>
            {
                var group = invocation.ParseResult.GetValueForOption(groupOption);
                var artifact = invocation.ParseResult.GetValueForOption(artifactPathOption);
                invocation.ExitCode = await orchestrator.DeployAsync(group!, artifact!.FullName);
            });
            try
            {
                logger.LogInfo($"🚀 {ToolName} v{ToolVersion} starting...");
                var exitCode = await rootCommand.InvokeAsync(args);
                if (exitCode == 0)
                {
                    logger.LogInfo($"✅ {ToolName} finished successfully.");
                }
                return exitCode;
            }
            catch (Exception ex)
            {
                var command = args.FirstOrDefault() ?? "unknown";
                string message;
                // CORRECTED: Scope of 'de' variable is fixed.
                if (ex is DataLinkException dataLinkEx)
                {
                    message = dataLinkEx.Message;
                }
                else
                {
                    message = ex.ToString();
                }

                await orchestrator.InvokeControlPointFailureAsync(command, message);
                HandleFatalException(logger, ex, $"command '{command}'");
                return ex is DataLinkException de ? (int)de.ExitCode : (int)ExitCode.UnhandledException;
            }
        }

        private static IHost BuildHost(DataLinkConfiguration config)
        {
            return Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(config);
                    services.AddSingleton<IAppLogger, Logger>();
                    services.AddHttpClient("ControlPointClient");
                    services.AddSingleton<IControlPointService, ControlPointService>();
                    services.AddSingleton<IDataLinkOrchestrator, DataLinkOrchestrator>();
                    services.AddSingleton<IGitService, GitService>();
                    services.AddSingleton<ILanguageAnalyzerFactory, LanguageAnalyzerFactory>();
                    services.AddSingleton<LanguageWeaverFactory>();
                    services.AddSingleton<ICodeWeaverService, CodeWeaverService>();
                    services.AddSingleton<CSharpAnalyzerService>();
                    services.AddSingleton<GoAnalyzerService>();
                    services.AddSingleton<JavaAnalyzerService>();
                    services.AddSingleton<JavaScriptAnalyzerService>();
                    services.AddSingleton<PythonAnalyzerService>();
                    services.AddSingleton<TypeScriptAnalyzerService>();
                })
                .ConfigureLogging(logging => logging.ClearProviders())
                .Build();
        }

        private static void HandleFatalException(IAppLogger logger, Exception ex, string activity)
        {
            if (ex is DataLinkException de)
            {
                logger.LogCritical($"FATAL ERROR [{de.ErrorCode}] during {activity}: {de.Message}");
            }
            else
            {
                logger.LogCritical($"An unexpected fatal error occurred during {activity}.", ex);
            }
        }
        private static async Task WritePipelineToolsLogAsync()
        {
            try
            {
                await ForensicLogger.WriteForensicLogEntryAsync(ToolName, ToolVersion);
            }
            catch (Exception ex)
            {
                // This is a non-critical operation; log to console and continue.
                Console.WriteLine($"[WARN] Could not write to pipeline-tools.log: {ex.Message}");
            }
        }
    }
}