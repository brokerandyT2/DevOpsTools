
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

namespace x3squaredcircles.SQLSentry.Container.Services.Http
{
    /// <summary>
    /// A lightweight, self-contained web server that runs as a background service.
    /// Its sole purpose is to serve documentation and example helper files directly from the container.
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
            // Suppress verbose hosting lifetime messages for a clean main console output
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();

            // Get port from environment or use a sensible default
            var port = Environment.GetEnvironmentVariable("GUARDIAN_DOCS_PORT") ?? "8080";
            var url = $"http://+:{port}";

            // --- Endpoint Mapping ---

            // Redirect root to /docs
            _app.MapGet("/", () => Results.Redirect("/docs"));

            // Main documentation endpoint
            _app.MapGet("/docs", () => Results.Content(ServeMarkdownAsHtml("README.md"), "text/html"));

            // Helper file endpoints
            _app.MapGet("/code/exceptions-example.json", () =>
                Results.File(GetEmbeddedResourceBytes("examples.guardian.exceptions.json"), "application/json", "guardian.exceptions.json"));

            _app.MapGet("/code/patterns-example.json", () =>
                Results.File(GetEmbeddedResourceBytes("examples.guardian.patterns.json"), "application/json", "guardian.patterns.json"));

            _logger.LogInformation("📚 Documentation server starting on {Url}. Available endpoints: /docs, /code/exceptions-example.json, /code/patterns-example.json", url);

            // Run the web server asynchronously. This task completes on shutdown.
            // This does not block the main application logic from running.
            return _app.RunAsync(url);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_app != null)
            {
                _logger.LogInformation("📚 Documentation server stopping.");
                await _app.StopAsync(cancellationToken);
            }
        }

        private byte[] GetEmbeddedResourceBytes(string resourceName)
        {
            var content = GetEmbeddedResourceContent(resourceName);
            return Encoding.UTF8.GetBytes(content);
        }

        private string GetEmbeddedResourceContent(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            // The resource name is constructed from the project's root namespace and the file's path.
            var fullResourceName = $"x3squaredcircles.SQLSentry.Container.Docs.{resourceName}";

            using var stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
            {
                _logger.LogWarning("Embedded resource '{ResourceName}' not found.", fullResourceName);
                return $"Error: Resource '{resourceName}' not found.";
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private string ServeMarkdownAsHtml(string resourceName)
        {
            var title = "3SC Guardian - Documentation";
            var markdown = GetEmbeddedResourceContent(resourceName);
            // This is a minimal-dependency markdown-to-html converter for display purposes.
            var content = new StringBuilder(HtmlEncoder.Default.Encode(markdown));
            content.Replace("&#13;&#10;", "<br/>").Replace("&#10;", "<br/>");
            // Basic header formatting
            content.Replace("### ", "<h3>").Replace("## ", "<h2>").Replace("# ", "<h1>");
            // Basic list formatting
            content.Replace("<br/>* ", "<li>").Replace("<br/>- ", "<li>");

            return $@"
<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'><title>{title}</title>
<style>
body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; line-height: 1.6; padding: 2em; max-width: 900px; margin: 0 auto; color: #333; }}
pre {{ background-color: #f6f8fa; padding: 16px; overflow: auto; border-radius: 6px; white-space: pre-wrap; word-wrap: break-word; font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, monospace;}}
code {{ font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, monospace; background: #eee; padding: 2px 4px; border-radius: 3px;}}
h1, h2, h3 {{ border-bottom: 1px solid #ddd; padding-bottom: 0.3em; }}
</style>
</head><body><pre>{content}</pre></body></html>";
        }
    }
}