using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Services;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container
{
    public static class Program
    {
        private static readonly string ToolName = "3SC DataLink";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public static async Task<int> Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // --- Configuration is registered first ---
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    // Now, we can resolve the config to build the logger
                    var configService = services.BuildServiceProvider().GetRequiredService<IConfigurationService>();
                    var config = configService.GetConfiguration();

                    // --- Register the Logger as a Singleton with its configuration ---
                    services.AddSingleton<IAppLogger>(new Logger(config));

                    // --- Register all other services ---
                    services.AddSingleton<IDataLinkOrchestrator, DataLinkOrchestrator>();
                    services.AddSingleton<ILanguageAnalyzerFactory, LanguageAnalyzerFactory>();
                    services.AddSingleton<CSharpAnalyzerService>();
                    // Other analyzers would be registered here
                    services.AddSingleton<ICodeWeaverService, CodeWeaverService>();
                    services.AddSingleton<IGitService, GitService>();
                    services.AddSingleton<ITestRunnerService, TestRunnerService>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    // Minimal default logging for the host itself, our custom logger handles the application logic.
                    logging.ClearProviders();
                    logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
                })
                .Build();

            var appLogger = host.Services.GetRequiredService<IAppLogger>();
            appLogger.LogInfo($"🚀 {ToolName} v{ToolVersion} starting...");

            try
            {
                var orchestrator = host.Services.GetRequiredService<IDataLinkOrchestrator>();
                var exitCode = await orchestrator.ExecuteAsync();

                if (exitCode == (int)ExitCode.Success)
                {
                    appLogger.LogInfo($"✅ {ToolName} finished successfully.");
                }
                else
                {
                    appLogger.LogWarning($"⚠️ {ToolName} finished with exit code: {exitCode}");
                }

                return exitCode;
            }
            catch (DataLinkException ex)
            {
                appLogger.LogCritical($"FATAL ERROR [{ex.ErrorCode}]: {ex.Message}");
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                appLogger.LogCritical("An unexpected fatal error occurred in the application.", ex);
                return (int)ExitCode.UnhandledException;
            }
            finally
            {
                appLogger.LogInfo($"🚀 {ToolName} shutting down.");
            }
        }
    }
}