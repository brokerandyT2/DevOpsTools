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

namespace x3squaredcircles.DesignToken.Generator.Services
{
    /// <summary>
    /// Defines the contract for the embedded HTTP server.
    /// </summary>
    public interface IHttpServerService
    {
        // The IHostedService interface provides StartAsync and StopAsync
    }

    /// <summary>
    /// An IHostedService that runs a lightweight ASP.NET Core server to provide documentation and helper files.
    /// This enhances developer experience by making the tool self-documenting and self-sufficient.
    /// </summary>
    public class HttpServerService : IHttpServerService, IHostedService
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
            // Suppress the default hosting lifetime messages for a cleaner console output.
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();

            var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
            var url = $"http://+:{port}";

            #region Endpoint Mapping

            _app.MapGet("/health", () => Results.Ok(new { status = "healthy", tool = "design-token-generator" }));

            _app.MapGet("/", () => Results.Redirect("/docs"));
            _app.MapGet("/docs", () => Results.Content(ServeMarkdownAsHtml("readme.md", "Design Token Generator - Docs"), "text/html"));

            _app.MapGet("/docs/android", () => Results.Content(ServeMarkdownAsHtml("android.readme.md", "Android Generation Docs"), "text/html"));
            _app.MapGet("/docs/ios", () => Results.Content(ServeMarkdownAsHtml("ios.readme.md", "iOS Generation Docs"), "text/html"));
            _app.MapGet("/docs/web", () => Results.Content(ServeMarkdownAsHtml("web.readme.md", "Web Generation Docs"), "text/html"));

            #endregion

            _logger.LogInformation("🎨 Documentation server starting on {Url}", url);

            // Run the web server. This task will complete when the application is shutting down.
            return _app.RunAsync(url);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_app != null)
            {
                _logger.LogInformation("Stopping documentation server.");
                await _app.StopAsync(cancellationToken);
            }
        }

        #region Content Serving Logic

        private string ServeMarkdownAsHtml(string resourceName, string title)
        {
            var markdown = GetEmbeddedResourceContent(resourceName);
            return ConvertMarkdownToHtml(markdown, title);
        }

        private string GetEmbeddedResourceContent(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullResourceName = $"x3squaredcircles.DesignToken.Generator.Docs.{resourceName.Replace("/", ".")}";

            using var stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
            {
                _logger.LogWarning("Embedded resource '{ResourceName}' not found.", fullResourceName);
                return $"Error: Resource '{resourceName}' not found.";
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private string ConvertMarkdownToHtml(string markdown, string title)
        {
            var content = new StringBuilder(HtmlEncoder.Default.Encode(markdown));
            // Basic replacements for readability. A library like Markdig would be used for a full implementation.
            content.Replace("&#13;&#10;", "<br/>").Replace("&#10;", "<br/>");
            content.Replace("---", "<hr/>");

            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <title>{HtmlEncoder.Default.Encode(title)}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; padding: 2em; max-width: 900px; margin: 0 auto; color: #333; }}
        pre {{ background-color: #f6f8fa; padding: 16px; overflow: auto; border-radius: 6px; white-space: pre-wrap; word-wrap: break-word; font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, monospace;}}
        hr {{ border: 0; height: 1px; background: #d1d5da; margin: 2em 0; }}
        a {{ color: #0366d6; text-decoration: none; }}
        a:hover {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <pre>{content}</pre>
</body>
</html>";
        }

        #endregion
    }
}