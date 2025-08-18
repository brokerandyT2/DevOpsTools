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
    /// Its purpose is to serve documentation (README) and language-specific DSL/helper files
    /// directly from the container's embedded resources.
    /// </summary>
    public class HttpServerService : IHostedService
    {
        private readonly ILogger<HttpServerService> _logger;
        private readonly DataLinkConfiguration _config;
        private WebApplication? _app;

        public HttpServerService(ILogger<HttpServerService> logger, DataLinkConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();
            // Suppress the default hosting lifetime messages for cleaner console output
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();

            var port = Environment.GetEnvironmentVariable("DATALINK_DOCS_PORT") ?? "8080";
            var url = $"http://+:{port}";

            #region Endpoint Mapping

            // Redirect root to /docs
            _app.MapGet("/", () => Results.Redirect("/docs"));

            // Main documentation endpoint
            _app.MapGet("/docs", () => Results.Content(ServeMarkdownAsHtml("README.md"), "text/html"));

            // Language-specific DSL helper files
            _app.MapGet("/code/csharp", () =>
                Results.File(GetEmbeddedResourceBytes("dsl.cs"), "text/plain", "ThreeScDsl.cs.txt"));

            _app.MapGet("/code/java", () =>
                Results.File(GetEmbeddedResourceBytes("dsl.java"), "text/plain", "ThreeScDsl.java.txt"));

            _app.MapGet("/code/python", () =>
                Results.File(GetEmbeddedResourceBytes("dsl.py"), "text/plain", "three_sc_dsl.py.txt"));

            _app.MapGet("/code/typescript", () =>
                Results.File(GetEmbeddedResourceBytes("dsl.ts"), "text/plain", "three_sc_dsl.ts.txt"));

            _app.MapGet("/code/go", () =>
                Results.File(GetEmbeddedResourceBytes("dsl.go"), "text/plain", "three_sc_dsl.go.txt"));

            #endregion

            _logger.LogInformation("📚 Documentation and DSL server starting on {Url}", url);

            // Run the web server asynchronously. This does not block the main application logic.
            return _app.RunAsync(url);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_app != null)
            {
                _logger.LogInformation("📚 Documentation and DSL server stopping.");
                await _app.StopAsync(cancellationToken);
            }
        }

        #region Content Serving Logic

        private byte[] GetEmbeddedResourceBytes(string resourceName)
        {
            var content = GetEmbeddedResourceContent(resourceName);
            return Encoding.UTF8.GetBytes(content);
        }

        private string GetEmbeddedResourceContent(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullResourceName = $"x3squaredcircles.datalink.container.Assets.{resourceName}";

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
            var title = "3SC DataLink - Documentation";
            var markdown = GetEmbeddedResourceContent(resourceName);
            // This is a minimal-dependency markdown-to-html converter for display purposes.
            var content = new StringBuilder(HtmlEncoder.Default.Encode(markdown));
            content.Replace("&#13;&#10;", "<br/>").Replace("&#10;", "<br/>");
            // Basic header formatting (assumes markdown headers like #, ##, ###)
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

        #endregion
    }
}