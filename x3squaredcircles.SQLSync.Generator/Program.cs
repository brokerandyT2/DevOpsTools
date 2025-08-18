using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Services;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator
{
    class Program
    {
        private static readonly string ToolName = Assembly.GetExecutingAssembly().GetName().Name?.ToString() ?? "sql-schema-generator";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        static async Task<int> Main(string[] args)
        {
            // Write to pipeline-tools.log immediately upon startup
            await WritePipelineToolsLogAsync();

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Register core services
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton<ILicenseClientService, LicenseClientService>();
                    services.AddSingleton<IKeyVaultService, KeyVaultService>();
                    services.AddSingleton<IGitOperationsService, GitOperationsService>();

                    // Register language analysis services
                    services.AddSingleton<ICSharpAnalyzerService, CSharpAnalyzerService>();
                    services.AddSingleton<IJavaAnalyzerService, JavaAnalyzerService>();
                    services.AddSingleton<IPythonAnalyzerService, PythonAnalyzerService>();
                    services.AddSingleton<IJavaScriptAnalyzerService, JavaScriptAnalyzerService>();
                    services.AddSingleton<ITypeScriptAnalyzerService, TypeScriptAnalyzerService>();
                    services.AddSingleton<IGoAnalyzerService, GoAnalyzerService>();
                    services.AddSingleton<ILanguageAnalyzerFactory, LanguageAnalyzerFactory>();

                    // Register database provider services
                    services.AddSingleton<ISqlServerProviderService, SqlServerProviderService>();
                    services.AddSingleton<IPostgreSqlProviderService, PostgreSqlProviderService>();
                    services.AddSingleton<IMySqlProviderService, MySqlProviderService>();
                    services.AddSingleton<IOracleProviderService, OracleProviderService>();
                    services.AddSingleton<ISqliteProviderService, SqliteProviderService>();
                    services.AddSingleton<IDatabaseProviderFactory, DatabaseProviderFactory>();

                    // Register schema processing services
                    services.AddSingleton<IEntityDiscoveryService, EntityDiscoveryService>();
                    services.AddSingleton<ISchemaAnalysisService, SchemaAnalysisService>();
                    services.AddSingleton<ISchemaValidationService, SchemaValidationService>();
                    services.AddSingleton<IRiskAssessmentService, RiskAssessmentService>();

                    // Register deployment services
                    services.AddSingleton<IDeploymentPlanService, DeploymentPlanService>();
                    services.AddSingleton<ISqlGenerationService, SqlGenerationService>();
                    services.AddSingleton<IBackupService, BackupService>();
                    services.AddSingleton<IDeploymentExecutionService, DeploymentExecutionService>();

                    // Register file management services
                    services.AddSingleton<IFileOutputService, FileOutputService>();
                    services.AddSingleton<ITagTemplateService, TagTemplateService>();
                    services.AddSingleton<ICustomScriptService, CustomScriptService>();

                    // Register main orchestrator
                    services.AddSingleton<ISqlSchemaOrchestrator, SqlSchemaOrchestrator>();

                    // Register this project's own HTTP server service
                    services.AddSingleton<IHttpServerService, HttpServerService>();

                    // Add HTTP client for external API communication
                    services.AddHttpClient();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();

                    var verbose = Environment.GetEnvironmentVariable("VERBOSE");
                    if (bool.TryParse(verbose, out var isVerbose) && isVerbose)
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    }
                    else
                    {
                        logging.SetMinimumLevel(LogLevel.Information);
                    }
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            CancellationTokenSource? httpServerCancellation = null;
            try
            {
                logger.LogInformation("🗄️ SQL Schema Generator v{Version} starting...", ToolVersion);

                var httpServer = host.Services.GetRequiredService<IHttpServerService>();
                httpServerCancellation = new CancellationTokenSource();
                _ = Task.Run(() => httpServer.StartHttpServerAsync(httpServerCancellation.Token), httpServerCancellation.Token);

                var orchestrator = host.Services.GetRequiredService<ISqlSchemaOrchestrator>();
                return await orchestrator.RunAsync();
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "A fatal error occurred in the SQL Schema Generator.");
                return (int)SqlSchemaExitCode.InvalidConfiguration; // General failure code
            }
            finally
            {
                httpServerCancellation?.Cancel();
                httpServerCancellation?.Dispose();
                logger.LogInformation("SQL Schema Generator shutting down.");
            }
        }

        private static async Task WritePipelineToolsLogAsync()
        {
            try
            {
                var outputDirectory = "/src"; // Container mount point
                var logEntry = $"{ToolName}={ToolVersion}";
                var logFilePath = Path.Combine(outputDirectory, "pipeline-tools.log");

                await File.AppendAllTextAsync(logFilePath, logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to write to pipeline-tools.log: {ex.Message}");
            }
        }
    }
}