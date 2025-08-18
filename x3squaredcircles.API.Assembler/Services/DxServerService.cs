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

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// An IHostedService that runs a lightweight ASP.NET Core server to provide documentation and language-specific DSL files.
    /// This enhances developer experience by making the tool self-documenting and self-sufficient.
    /// </summary>
    public class DxServerService : IHostedService
    {
        private readonly ILogger<DxServerService> _logger;
        private WebApplication? _app;

        public DxServerService(ILogger<DxServerService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();

            var port = Environment.GetEnvironmentVariable("ASSEMBLER_DX_PORT") ?? "8080";
            var url = $"http://+:{port}";

            #region Endpoint Mapping

            _app.MapGet("/health", () => Results.Ok(new { status = "healthy", tool = "3sc-api-assembler" }));

            _app.MapGet("/", () => Results.Redirect("/docs"));
            _app.MapGet("/docs", () => Results.Content(ServeMarkdownAsHtml("readme.md", "3SC API Assembler - Documentation")));

            // Language-specific documentation endpoints
            _app.MapGet("/docs/csharp", () => Results.Content(ServeMarkdownAsHtml("readme.md", "C# Documentation")));
            _app.MapGet("/docs/java", () => Results.Content(ServeMarkdownAsHtml("readme.md", "Java Documentation")));
            _app.MapGet("/docs/python", () => Results.Content(ServeMarkdownAsHtml("readme.md", "Python Documentation")));
            _app.MapGet("/docs/typescript", () => Results.Content(ServeMarkdownAsHtml("readme.md", "TypeScript Documentation")));
            _app.MapGet("/docs/go", () => Results.Content(ServeMarkdownAsHtml("readme.md", "Go Documentation")));
            _app.MapGet("/docs/javascript", () => Results.Content(ServeMarkdownAsHtml("readme.md", "JavaScript Documentation")));

            // Language-specific DSL file endpoints (served as plain text)
            _app.MapGet("/code/csharp", () => ServeDslFile("ThreeScDsl.cs.txt", "ThreeScDsl.cs"));
            _app.MapGet("/code/java", () => ServeDslFile("ThreeScDsl.java.txt", "ThreeScDsl.java"));
            _app.MapGet("/code/python", () => ServeDslFile("three_sc_dsl.py.txt", "three_sc_dsl.py"));
            _app.MapGet("/code/typescript", () => ServeDslFile("three-sc.dsl.ts.txt", "three-sc.dsl.ts"));
            _app.MapGet("/code/go", () => ServeDslFile("three_sc_dsl.go.txt", "three_sc_dsl.go"));
            _app.MapGet("/code/javascript", () => ServeDslFile("three_sc_dsl.js.txt", "three_sc_dsl.js"));

            #endregion

            _logger.LogInformation("🚀 DX Server starting on {Url}", url);

            return _app.RunAsync(url);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_app != null)
            {
                _logger.LogInformation("Stopping DX Server.");
                await _app.StopAsync(cancellationToken);
            }
        }

        #region Content Serving Logic

        private IResult ServeDslFile(string resourceName, string downloadFileName)
        {
            var content = GetEmbeddedResourceContent(resourceName);
            if (content.StartsWith("Error:"))
            {
                return Results.NotFound(content);
            }
            // Use Results.File to set the Content-Disposition header for a nice download experience.
            return Results.File(Encoding.UTF8.GetBytes(content), "text/plain", downloadFileName);
        }

        private string ServeMarkdownAsHtml(string resourceName, string title)
        {
            var markdown = GetEmbeddedResourceContent(resourceName);
            return ConvertMarkdownToHtml(markdown, title);
        }

        private string GetEmbeddedResourceContent(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullResourceName = $"x3squaredcircles.API.Assembler.Dsl.{resourceName}";

            using var stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
            {
                var manifestResources = assembly.GetManifestResourceNames();
                _logger.LogWarning("Embedded resource '{ResourceName}' not found. Available resources: {AvailableResources}", fullResourceName, string.Join(", ", manifestResources));
                return $"Error: Resource '{resourceName}' not found.";
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private string ConvertMarkdownToHtml(string markdown, string title)
        {
            var content = new StringBuilder(HtmlEncoder.Default.Encode(markdown));
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