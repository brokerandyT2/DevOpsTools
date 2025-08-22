using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Configuration;
using x3squaredcircles.SQLSync.Generator.Models;
using x3squaredcircles.SQLSync.Generator.Observability;
using x3squaredcircles.SQLSync.Generator.Services;

namespace x3squaredcircles.SQLSync.Generator
{
    class Program
    {
        private static readonly string ToolName = Assembly.GetExecutingAssembly().GetName().Name?.ToString() ?? "sql-schema-generator";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        static async Task<int> Main(string[] args)
        {
            await WritePipelineToolsLogAsync();

            var config = EnvironmentConfigurationLoader.LoadConfiguration();

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(config);

                    // Register Orchestrator and Core Services
                    services.AddSingleton<ISqlSchemaOrchestrator, SqlSchemaOrchestrator>();
                    services.AddSingleton<ILicenseClientService, LicenseClientService>();
                    services.AddSingleton<IKeyVaultService, KeyVaultService>();
                    services.AddSingleton<IGitOperationsService, GitOperationsService>();
                    services.AddSingleton<IEntityDiscoveryService, EntityDiscoveryService>();
                    services.AddSingleton<ISchemaAnalysisService, SchemaAnalysisService>();
                    services.AddSingleton<ISchemaValidationService, SchemaValidationService>();
                    services.AddSingleton<IRiskAssessmentService, RiskAssessmentService>();
                    services.AddSingleton<IDeploymentPlanService, DeploymentPlanService>();
                    services.AddSingleton<ISqlGenerationService, SqlGenerationService>();
                    services.AddSingleton<IBackupService, BackupService>();
                    services.AddSingleton<IDeploymentExecutionService, DeploymentExecutionService>();
                    services.AddSingleton<IFileOutputService, FileOutputService>();
                    services.AddSingleton<ITagTemplateService, TagTemplateService>();
                    services.AddSingleton<ICustomScriptService, CustomScriptService>();
                    services.AddSingleton<IControlPointService, ControlPointService>();

                    // Register Language Analyzer Services and Factory
                    services.AddSingleton<ICSharpAnalyzerService, CSharpAnalyzerService>();
                    services.AddSingleton<IJavaAnalyzerService, JavaAnalyzerService>();
                    services.AddSingleton<IPythonAnalyzerService, PythonAnalyzerService>();
                    services.AddSingleton<IJavaScriptAnalyzerService, JavaScriptAnalyzerService>();
                    services.AddSingleton<ITypeScriptAnalyzerService, TypeScriptAnalyzerService>();
                    services.AddSingleton<IGoAnalyzerService, GoAnalyzerService>();
                    services.AddSingleton<ILanguageAnalyzerFactory, LanguageAnalyzerFactory>();

                    // Register Database Provider Services and Factory
                    services.AddSingleton<ISqlServerProviderService, SqlServerProviderService>();
                    services.AddSingleton<IPostgreSqlProviderService, PostgreSqlProviderService>();
                    services.AddSingleton<IMySqlProviderService, MySqlProviderService>();
                    services.AddSingleton<IOracleProviderService, OracleProviderService>();
                    services.AddSingleton<ISqliteProviderService, SqliteProviderService>();
                    services.AddSingleton<IDatabaseProviderFactory, DatabaseProviderFactory>();

                    // Register Authentication Strategy Services and Factory
                    services.AddSingleton<UsernamePasswordStrategy>();
                    services.AddSingleton<IAuthenticationStrategyFactory, AuthenticationStrategyFactory>();

                    // Register DX Server as a Hosted Service
                    services.AddHostedService<DocumentationService>();

                    services.AddHttpClient();
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

                var orchestrator = host.Services.GetRequiredService<ISqlSchemaOrchestrator>();
                var result = await orchestrator.RunAsync();

                logger.LogInformation("✅ {ToolName} completed with exit code: {ExitCode}", ToolName, result);

                await host.StopAsync(TimeSpan.FromSeconds(10));
                return result;
            }
            catch (SqlSchemaException ex)
            {
                logger.LogError(ex, "A controlled application error occurred: {Message}", ex.Message);
                await host.StopAsync();
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "An unhandled exception occurred, terminating the application.");
                await host.StopAsync();
                return (int)SqlSchemaExitCode.UnhandledException;
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