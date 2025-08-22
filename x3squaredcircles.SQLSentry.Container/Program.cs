using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.SQLSentry.Container.Services;
using x3squaredcircles.SQLSentry.Container.Services.Http;
using x3squaredcircles.SQLSentry.Container.Models;

namespace x3squaredcircles.SQLSentry.Container
{
    /// <summary>
    /// Main entry point for the 3SC Guardian Data Governance Engine.
    /// Sets up the application host, dependency injection, logging, and initiates the orchestration service.
    /// </summary>
    public static class Program
    {
        private static readonly string ToolName = "3SC Guardian";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "8.0.0";

        public static async Task<int> Main(string[] args)
        {
            WritePipelineToolsLogAsync();
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // --- Core Services ---
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton<IGuardianOrchestrator, GuardianOrchestrator>();

                    // --- External Integration Services ---
                    services.AddSingleton<IGitClientService, GitClientService>();
                    services.AddSingleton<IKeyVaultService, KeyVaultService>();
                    services.AddHttpClient(); // For KeyVaultService and future integrations

                    // --- Data Processing & Analysis Services ---
                    services.AddSingleton<ISqlDeltaParserService, SqlDeltaParserService>();
                    services.AddSingleton<IDatabaseScannerService, DatabaseScannerService>();
                    services.AddSingleton<IRulesEngineService, RulesEngineService>();
                    services.AddSingleton<IReportGeneratorService, ReportGeneratorService>();

                    // --- Background HTTP Server for Docs & Helper Files ---
                    services.AddHostedService<HttpServerService>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options => options.FormatterName = "Simple")
                           .AddSimpleConsole(options =>
                           {
                               options.IncludeScopes = false;
                               options.SingleLine = true;
                               options.TimestampFormat = "HH:mm:ss ";
                           });

                    // Allow log level to be controlled by an environment variable for debugging
                    var logLevelEnv = Environment.GetEnvironmentVariable("GUARDIAN_LOG_LEVEL");
                    var logLevel = Enum.TryParse<LogLevel>(logLevelEnv, true, out var level) ? level : LogLevel.Information;
                    logging.SetMinimumLevel(logLevel);
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("🛡️  {ToolName} v{ToolVersion} starting...", ToolName, ToolVersion);

            try
            {
                var orchestrator = host.Services.GetRequiredService<IGuardianOrchestrator>();
                var exitCode = await orchestrator.ExecuteAsync();

                if (exitCode == (int)ExitCode.Success)
                {
                    logger.LogInformation("✅  {ToolName} finished successfully.", ToolName);
                }
                else
                {
                    logger.LogWarning("⚠️  {ToolName} finished with violations. Exit Code: {ExitCode}", ToolName, exitCode);
                }

                return exitCode;
            }
            catch (GuardianException ex)
            {
                logger.LogCritical("❌ FATAL [Code: {ErrorCode}]: {ErrorMessage}", ex.ErrorCode, ex.Message);
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "❌ An unexpected fatal error occurred.");
                return (int)ExitCode.UnhandledException;
            }
            finally
            {
                logger.LogInformation("🛡️  {ToolName} shutting down.", ToolName);
            }
        }
        private static async Task WritePipelineToolsLogAsync()
        {
            try
            {
                ForensicLogger.WriteForensicLogEntryAsync(ToolName, ToolVersion);
            }
            catch (Exception ex)
            {
                // This is a non-critical operation; log to console and continue.
                Console.WriteLine($"[WARN] Could not write to pipeline-tools.log: {ex.Message}");
            }
        }
    }

}