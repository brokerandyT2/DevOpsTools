using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// A self-contained, lightweight web server that runs as a background service.
    /// Its purpose is to serve documentation (README), health checks, and language-specific DSL/helper files
    /// directly from the container's embedded resources.
    /// </summary>
    public class HttpServerService : IHostedService
    {
        private readonly ILogger<HttpServerService> _logger;
        private WebApplication? _app;

        public HttpServerService(ILogger<HttpServerService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();

            var port = Environment.GetEnvironmentVariable("DATALINK_DOCS_PORT") ?? "8080";
            var url = $"http://+:{port}";

            #region Endpoint Mapping

            _app.MapGet("/", () => Results.Redirect("/docs"));

            _app.MapGet("/health", () => Results.Json(new { status = "healthy" }));

            // Serves the main, top-level readme.md
            _app.MapGet("/docs", () => Results.Content(ServeMarkdownAsHtml("readme.md"), "text/html"));

            // Serves the language-specific readme.md from the /Docs/{language}/ folder
            _app.MapGet("/docs/{language}", (string language) =>
            {
                var resourceName = $"Docs.{language.ToLowerInvariant()}.readme.md";
                return Results.Content(ServeMarkdownAsHtml(resourceName), "text/html");
            });

            // Serves the language-specific DSL file from the /code/{language}/ folder
            _app.MapGet("/code/{language}", (string language) =>
            {
                var (fileName, downloadName) = GetDslFileInfoForLanguage(language);

                if (string.IsNullOrEmpty(fileName))
                {
                    return Results.NotFound($"No DSL file found for language: {language}");
                }

                var resourceName = $"code.{language.ToLowerInvariant()}.{fileName}";
                var fileBytes = GetEmbeddedResourceBytes(resourceName);

                return fileBytes.Length > 0
                    ? Results.File(fileBytes, "text/plain", downloadName)
                    : Results.NotFound($"Resource '{resourceName}' could not be loaded.");
            });

            #endregion

            _logger.LogInformation("📚 DX Server (Docs, Health, DSL) starting on {Url}", url);

            return _app.RunAsync(url);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_app != null)
            {
                _logger.LogInformation("📚 DX Server stopping.");
                await _app.StopAsync(cancellationToken);
            }
        }

        #region Content Serving Logic

        private (string ResourceFileName, string DownloadFileName) GetDslFileInfoForLanguage(string language)
        {
            return language.ToLowerInvariant() switch
            {
                "csharp" => ("ThreeScDsl.cs.txt", "ThreeScDsl.cs"),
                "go" => ("three_sc_dsl.go.txt", "three_sc_dsl.go"),
                "java" => ("ThreeScDsl.java.txt", "ThreeScDsl.java"),
                "javascript" => ("three_sc_dsl.js.txt", "three_sc_dsl.js"),
                "python" => ("three_sc_dsl.py.txt", "three_sc_dsl.py"),
                "typescript" => ("three_sc_dsl.ts.txt", "three_sc_dsl.ts"),
                _ => (string.Empty, string.Empty)
            };
        }

        private byte[] GetEmbeddedResourceBytes(string resourceNameSuffix)
        {
            var content = GetEmbeddedResourceContent(resourceNameSuffix);
            return Encoding.UTF8.GetBytes(content);
        }

        private string GetEmbeddedResourceContent(string resourceNameSuffix)
        {
            var assembly = Assembly.GetExecutingAssembly();
            // In .NET, embedded resource paths use '.' as a separator for folders.
            // Assuming a root "Assets" folder in the project for all embedded resources.
            var fullResourceName = $"x3squaredcircles.datalink.container.Assets.{resourceNameSuffix}";

            using var stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
            {
                _logger.LogWarning("Embedded resource '{ResourceName}' not found.", fullResourceName);
                // Return a clear error message in the content if the resource is missing.
                return $"# Error: Resource Not Found\n\nThe resource specified ('{resourceNameSuffix}') could not be found in the application's embedded assets. Please check the file path and ensure it is marked as an 'Embedded Resource' in the project file.";
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private string ServeMarkdownAsHtml(string resourceNameSuffix)
        {
            var title = $"3SC DataLink - {resourceNameSuffix.Replace(".md", "")}";
            var markdown = GetEmbeddedResourceContent(resourceNameSuffix);

            var content = new StringBuilder(HtmlEncoder.Default.Encode(markdown));
            content.Replace("&#13;&#10;", "<br/>").Replace("&#10;", "<br/>");
            content.Replace("### ", "<h3>").Replace("## ", "<h2>").Replace("# ", "<h1>");
            content.Replace("<br/>* ", "<li>").Replace("<br/>- ", "<li>");
            content.Replace("**", "</b>").Replace("<b>", "<b>");

            return $@"
<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'><title>{title}</title>
<style>
body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; line-height: 1.6; padding: 2em; max-width: 900px; margin: 0 auto; color: #333; background-color: #fdfdfd; }}
pre {{ background-color: #f6f8fa; padding: 16px; overflow: auto; border-radius: 6px; white-space: pre-wrap; word-wrap: break-word; font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, monospace; border: 1px solid #ddd; }}
code {{ font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, monospace; background: #eee; padding: 2px 4px; border-radius: 3px;}}
h1, h2, h3 {{ border-bottom: 1px solid #ddd; padding-bottom: 0.3em; }}
li {{ margin-left: 20px; }}
</style>
</head><body><pre>{content}</pre></body></html>";
        }

        #endregion
    }
}