using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.VersionDetective.Container.Services;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container
{
    class Program
    {
        private static readonly string ToolName = Assembly.GetExecutingAssembly().GetName().Name?.ToString() ?? "version-detective";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        static async Task<int> Main(string[] args)
        {
            CancellationTokenSource? httpServerCancellation = null;

            try
            {
                // Write to pipeline-tools.log immediately upon startup
                await WritePipelineToolsLogAsync();

                // Create host builder
                var host = Host.CreateDefaultBuilder(args)
                    .ConfigureServices((context, services) =>
                    {
                        // Register services
                        services.AddSingleton<IConfigurationService, ConfigurationService>();
                        services.AddSingleton<ILicenseClientService, LicenseClientService>();
                        services.AddSingleton<IGitAnalysisService, GitAnalysisService>();
                        services.AddSingleton<ILanguageAnalysisService, LanguageAnalysisService>();
                        services.AddSingleton<IVersionCalculationService, VersionCalculationService>();
                        services.AddSingleton<ITagTemplateService, TagTemplateService>();
                        services.AddSingleton<IOutputService, OutputService>();
                        services.AddSingleton<IKeyVaultService, KeyVaultService>();
                        services.AddSingleton<IDocumentationService, DocumentationService>();
                        services.AddSingleton<IVersionDetectiveOrchestrator, VersionDetectiveOrchestrator>();

                        // Add HTTP client for license server communication
                        services.AddHttpClient();
                    })
                    .ConfigureLogging((context, logging) =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();

                        // Set log level based on VERBOSE environment variable
                        var verbose = Environment.GetEnvironmentVariable("VERBOSE");
                        var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");

                        if (bool.TryParse(verbose, out var isVerbose) && isVerbose)
                        {
                            logging.SetMinimumLevel(LogLevel.Debug);
                        }
                        else if (Enum.TryParse<LogLevel>(logLevel, true, out var level))
                        {
                            logging.SetMinimumLevel(level);
                        }
                        else
                        {
                            logging.SetMinimumLevel(LogLevel.Information);
                        }
                    })
                    .Build();

                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("🔍 Version Detective Container v{Version} starting...", ToolVersion);

                // Start documentation HTTP server
                var documentationService = host.Services.GetRequiredService<IDocumentationService>();
                httpServerCancellation = new CancellationTokenSource();

                var httpServerTask = Task.Run(() => documentationService.StartHttpServerAsync(httpServerCancellation.Token));
                logger.LogInformation("📚 Documentation server starting on port 8080...");

                // Get orchestrator and run main application
                var orchestrator = host.Services.GetRequiredService<IVersionDetectiveOrchestrator>();
                var exitCode = await orchestrator.RunAsync();

                logger.LogInformation("Version Detective Container completed with exit code: {ExitCode}", exitCode);
                return exitCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return 1; // Invalid configuration
            }
            finally
            {
                // Stop HTTP server
                try
                {
                    if (httpServerCancellation != null)
                    {
                        httpServerCancellation.Cancel();
                        httpServerCancellation.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error stopping HTTP server: {ex.Message}");
                }
            }
        }

        private static async Task WritePipelineToolsLogAsync()
        {
            try
            {
                var outputDirectory = "/src"; // Container mount point
                var logEntry = $"{ToolName}={ToolVersion}";
                var logFilePath = Path.Combine(outputDirectory, "pipeline-tools.log");

                // Append to log file (create if doesn't exist)
                await File.AppendAllTextAsync(logFilePath, logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Don't fail startup if pipeline tools log fails
                Console.WriteLine($"Warning: Failed to write pipeline-tools.log: {ex.Message}");
            }
        }
    }
}