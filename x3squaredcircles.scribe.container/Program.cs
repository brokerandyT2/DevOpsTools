using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Configuration;
using x3squaredcircles.scribe.container.Hosting;
using x3squaredcircles.scribe.container.Services;

namespace x3squaredcircles.scribe.container
{
    /// <summary>
    /// The main entry point for The Scribe application.
    /// </summary>
    public static class Program
    {
        private const string DocsFileName = "README.md";

        public static async Task<int> Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Trace);

            #region Configuration & DI Registration

            builder.Services.AddHttpClient();

            builder.Services.AddOptions<ScribeSettings>()
                .Configure<IConfiguration>((settings, config) =>
                {
                    settings.WikiRootPath = config["SCRIBE_WIKI_ROOT_PATH"] ?? string.Empty;
                    settings.AppName = config["SCRIBE_APP_NAME"] ?? string.Empty;
                    settings.WorkItemStyle = config["SCRIBE_WORK_ITEM_STYLE"] ?? "list";
                    settings.WorkItemUrl = config["SCRIBE_WI_URL"];
                    settings.WorkItemPat = config["SCRIBE_WI_PAT"];
                    settings.WorkItemProvider = config["SCRIBE_WI_PROVIDER"];
                })
                .ValidateDataAnnotations();

            // Register all application services with the DI container.
            builder.Services.AddTransient<IOutputManagerService, OutputManagerService>();
            builder.Services.AddTransient<IForensicLogService, ForensicLogService>();
            builder.Services.AddTransient<IArtifactDiscoveryService, ArtifactDiscoveryService>();
            builder.Services.AddTransient<IMarkdownGenerationService, MarkdownGenerationService>();
            builder.Services.AddTransient<IWorkItemParserService, WorkItemParserService>();
            builder.Services.AddTransient<IWorkItemProviderManager, WorkItemProviderManager>();
            builder.Services.AddTransient<IEnvironmentService, EnvironmentService>();
            builder.Services.AddTransient<IGitService, GitService>();
            builder.Services.AddTransient<IFirehoseService, FirehoseService>();

            // Register all concrete implementations of IWorkItemProvider.
            builder.Services.AddTransient<IWorkItemProvider, GitHubWorkItemProvider>();
            builder.Services.AddTransient<IWorkItemProvider, AzureDevOpsWorkItemProvider>();
            builder.Services.AddTransient<IWorkItemProvider, JiraWorkItemProvider>();

            // Register the Scribe's core logic service.
            builder.Services.AddHostedService<ScribeHostedService>();
            #endregion

            var app = builder.Build();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            app.MapGet("/docs", async () =>
            {
                if (File.Exists(DocsFileName))
                {
                    var content = await File.ReadAllTextAsync(DocsFileName);
                    return Results.Content(content, "text/markdown");
                }
                logger.LogWarning("{FileName} not found. The /docs endpoint will return a 404.", DocsFileName);
                return Results.NotFound($"{DocsFileName} not found. Please ensure it exists in the application root.");
            });

            #region Global Exception Handler & Host Execution
            try
            {
                logger.LogInformation("3SC Scribe Host starting up...");
                await app.RunAsync();
                logger.LogInformation("3SC Scribe Host shut down gracefully.");
                return 0; // Success
            }
            catch (OptionsValidationException ex)
            {
                logger.LogCritical("Configuration validation failed. The Scribe cannot start.");
                foreach (var failure in ex.Failures)
                {
                    logger.LogCritical("- {Failure}", failure);
                }
                return -2;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "A critical, unhandled exception occurred. The Scribe Host is terminating.");
                return -1;
            }
            #endregion
        }

    }
}