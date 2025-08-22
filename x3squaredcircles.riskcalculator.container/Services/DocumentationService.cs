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

namespace x3squaredcircles.RiskCalculator.Container.Services
{
    /// <summary>
    /// An IHostedService that runs a lightweight ASP.NET Core server to provide documentation and health checks,
    /// conforming to the standard DX Server architecture.
    /// </summary>
    public class DocumentationService : IHostedService
    {
        private readonly ILogger<DocumentationService> _logger;
        private WebApplication? _app;

        public DocumentationService(ILogger<DocumentationService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();

            var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
            var url = $"http://+:{port}";

            _app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "risk-calculator" }));
            _app.MapGet("/", () => Results.Redirect("/docs"));
            _app.MapGet("/docs", () => Results.Content(ServeMarkdownAsHtml("Docs/readme.md", "Risk Calculator Documentation"), "text/html"));

            _logger.LogInformation("📚 DX Server starting on {Url}", url);

            // Do not block the startup process. The WebApplication runs in the background.
            _ = _app.RunAsync(url);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_app != null)
            {
                _logger.LogInformation("Stopping DX Server.");
                await _app.StopAsync(cancellationToken);
            }
        }

        private string ServeMarkdownAsHtml(string resourceName, string title)
        {
            try
            {
                var markdown = GetEmbeddedResourceContent(resourceName);
                return ConvertMarkdownToHtml(markdown, title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to serve markdown file '{ResourceName}'", resourceName);
                return $"<h1>Error</h1><p>Could not load documentation: {ex.Message}</p>";
            }
        }

        private string GetEmbeddedResourceContent(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullResourceName = $"{assembly.GetName().Name}.{resourceName.Replace("/", ".")}";

            using var stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
            {
                throw new FileNotFoundException($"Embedded resource '{fullResourceName}' not found. Ensure the readme.md file's 'Build Action' is set to 'Embedded resource'.");
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private string ConvertMarkdownToHtml(string markdown, string title)
        {
            // A more advanced implementation would use a proper Markdown parsing library.
            // For this tool, a simple pre-formatted block is sufficient and avoids extra dependencies.
            var content = HtmlEncoder.Default.Encode(markdown);

            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <title>{HtmlEncoder.Default.Encode(title)}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; padding: 2em; max-width: 900px; margin: 0 auto; color: #333; }}
        pre {{ background-color: #f6f8fa; padding: 16px; overflow: auto; border-radius: 6px; white-space: pre-wrap; word-wrap: break-word; font-family: 'SFMono-Regular', Consolas, monospace; }}
        h1, h2, h3 {{ border-bottom: 1px solid #eaecef; padding-bottom: .3em; }}
    </style>
</head>
<body>
    <pre>{content}</pre>
</body>
</html>";
        }
    }
}