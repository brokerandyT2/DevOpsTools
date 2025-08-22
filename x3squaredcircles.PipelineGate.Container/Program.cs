using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.PipelineGate.Container.Configuration;
using x3squaredcircles.PipelineGate.Container.Models;
using x3squaredcircles.PipelineGate.Container.Observability;
using x3squaredcircles.PipelineGate.Container.Services;

namespace x3squaredcircles.PipelineGate.Container
{
    class Program
    {
        private static readonly string ToolName = Assembly.GetExecutingAssembly().GetName().Name?.ToString() ?? "pipeline-gate-controller";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        static async Task<int> Main(string[] args)
        {
            await WritePipelineToolsLogAsync();

            var config = EnvironmentConfigurationLoader.LoadConfiguration();

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(config);

                    // Register application services
                    services.AddSingleton<IPipelineGateOrchestrator, PipelineGateOrchestrator>();
                    services.AddSingleton<IHttpService, HttpService>();
                    services.AddSingleton<IEvaluationEngine, EvaluationEngine>();
                    services.AddSingleton<IKeyVaultService, KeyVaultService>();
                    services.AddSingleton<IControlPointService, ControlPointService>();

                    // Register DX Server as a Hosted Service
                    services.AddHostedService<DocumentationService>();

                    services.AddHttpClient("GateClient", client =>
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{ToolName}/{ToolVersion}");
                    });
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(config.Logging.LogLevel);

                    if (!string.IsNullOrWhiteSpace(config.Observability.FirehoseLogEndpointUrl) &&
                        !string.IsNullOrWhiteSpace(config.Observability.FirehoseLogEndpointToken))
                    {
                        logging.AddProvider(new FirehoseLoggerProvider(
                            config,
                            new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>()
                        ));
                    }
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            try
            {
                await host.StartAsync();
                logger.LogInformation("🚀 {ToolName} v{ToolVersion} starting...", ToolName, ToolVersion);

                if (!string.IsNullOrWhiteSpace(config.Observability.FirehoseLogEndpointUrl))
                {
                    logger.LogInformation("🔥 Firehose logging is active.");
                }

                var orchestrator = host.Services.GetRequiredService<IPipelineGateOrchestrator>();
                var exitCode = await orchestrator.RunAsync();

                logger.LogInformation("✅ {ToolName} completed with exit code: {ExitCode} ({ExitCodeName})", ToolName, exitCode, (GateExitCode)exitCode);

                await host.StopAsync(TimeSpan.FromSeconds(10));
                return exitCode;
            }
            catch (PipelineGateException ex)
            {
                logger.LogError(ex, "A controlled application error occurred: {Message}", ex.Message);
                await host.StopAsync();
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "An unhandled exception occurred, terminating the application.");
                await host.StopAsync();
                return (int)GateExitCode.UnhandledException;
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