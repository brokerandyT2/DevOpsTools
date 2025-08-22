using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Core;
using x3squaredcircles.MobileAdapter.Generator.Discovery;
using x3squaredcircles.MobileAdapter.Generator.Generation;
using x3squaredcircles.MobileAdapter.Generator.Licensing;
using x3squaredcircles.MobileAdapter.Generator.Models;
using x3squaredcircles.MobileAdapter.Generator.Observability;
using x3squaredcircles.MobileAdapter.Generator.Services;
using x3squaredcircles.MobileAdapter.Generator.TypeMapping;

namespace x3squaredcircles.MobileAdapter.Generator
{
    class Program
    {
        private static readonly string ToolName = Assembly.GetExecutingAssembly().GetName().Name ?? "mobile-adapter-generator";
        private static readonly string ToolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        static async Task<int> Main(string[] args)
        {
            // Immediately log tool execution for pipeline forensics, even before DI is set up.
            await WritePipelineToolsLogAsync();

            var config = EnvironmentConfigurationLoader.LoadConfiguration();

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Register the main application orchestrator
                    services.AddSingleton<AdapterGeneratorEngine>();

                    // Register configuration and validation services
                    services.AddSingleton(config); // Use the pre-loaded config
                    services.AddSingleton<ConfigurationValidator>();

                    // Register core services
                    services.AddSingleton<LicenseManager>();
                    services.AddSingleton<TypeMappingEngine>();
                    services.AddSingleton<IGitOperationsService, GitOperationsService>();
                    services.AddSingleton<ITagTemplateService, TagTemplateService>();
                    services.AddSingleton<IFileOutputService, FileOutputService>();
                    services.AddSingleton<IControlPointService, ControlPointService>();
                    services.AddSingleton<IPlaceholderResolverService, PlaceholderResolverService>();

                    // Register discovery engine factory and language-specific engines
                    services.AddSingleton<IClassDiscoveryEngineFactory, ClassDiscoveryEngineFactory>();
                    services.AddTransient<CSharpDiscoveryEngine>();
                    services.AddTransient<JavaDiscoveryEngine>();
                    services.AddTransient<KotlinDiscoveryEngine>();
                    services.AddTransient<JavaScriptDiscoveryEngine>();
                    services.AddTransient<TypeScriptDiscoveryEngine>();
                    services.AddTransient<PythonDiscoveryEngine>();
                    services.AddTransient<GoDiscoveryEngine>();

                    // Register code generator factory and platform-specific generators
                    services.AddSingleton<ICodeGeneratorFactory, CodeGeneratorFactory>();
                    services.AddTransient<AndroidCodeGenerator>();
                    services.AddTransient<IosCodeGenerator>();

                    // Add HTTP client for services that need it
                    services.AddHttpClient();

                    // Register the embedded web server as a hosted service
                    services.AddHostedService<LanguageAttributeService>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
                    logging.SetMinimumLevel(config.LogLevel);

                    // Add the Firehose logger provider only if it is fully configured.
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

                logger.LogInformation("🚀 {ToolName} v{ToolVersion} starting main execution...", ToolName, ToolVersion);

                // The status of the firehose logger is determined by the configuration.
                if (!string.IsNullOrWhiteSpace(config.Observability.FirehoseLogEndpointUrl))
                {
                    logger.LogInformation("🔥 Firehose logging is active.");
                }

                var engine = host.Services.GetRequiredService<AdapterGeneratorEngine>();
                var result = await engine.GenerateAdaptersAsync();

                if (result.Success)
                {
                    logger.LogInformation("✅ {ToolName} completed successfully. Generated {FileCount} files.", ToolName, result.GeneratedFiles.Count);
                }
                else
                {
                    logger.LogError("❌ {ToolName} failed: {ErrorMessage}", ToolName, result.ErrorMessage);
                }

                // Host shutdown will correctly dispose the logger factory, which in turn disposes the provider.
                await host.StopAsync(TimeSpan.FromSeconds(10));

                return (int)result.ExitCode;
            }
            catch (MobileAdapterException ex)
            {
                logger.LogError(ex, "A configuration or operational error occurred: {Message}", ex.Message);
                await host.StopAsync();
                return (int)ex.ExitCode;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "An unhandled exception occurred, terminating the application.");
                await host.StopAsync();
                return (int)MobileAdapterException.UnhandledException;
            }
        }

        /// <summary>
        /// Appends the tool's name and version to the pipeline-tools.log file.
        /// This creates a forensic record of tool execution within the CI/CD environment.
        /// </summary>
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