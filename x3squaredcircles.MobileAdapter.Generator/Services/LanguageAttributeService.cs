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

namespace x3squaredcircles.MobileAdapter.Generator.Services
{
    /// <summary>
    /// An IHostedService that runs a lightweight ASP.NET Core server to provide documentation and language-specific helper files.
    /// </summary>
    public class LanguageAttributeService : IHostedService
    {
        private readonly ILogger<LanguageAttributeService> _logger;
        private WebApplication? _app;

        public LanguageAttributeService(ILogger<LanguageAttributeService> logger)
        {
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();
            // Suppress the default hosting lifetime messages for a cleaner console output
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();

            // Get port from environment or default
            var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
            var url = $"http://+:{port}";

            #region Endpoint Mapping

            // Health check
            _app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

            // Main documentation redirect and page
            _app.MapGet("/", () => Results.Redirect("/docs"));
            _app.MapGet("/docs", () => Results.Content(GetLanguageNavigationHtml(), "text/html"));

            // Language-specific documentation endpoints
            _app.MapGet("/docs/csharp", () => Results.Content(ServeMarkdownAsHtml("csharp/readme.md", "C# Documentation"), "text/html"));
            _app.MapGet("/docs/java", () => Results.Content(ServeMarkdownAsHtml("java/readme.md", "Java Documentation"), "text/html"));
            _app.MapGet("/docs/python", () => Results.Content(ServeMarkdownAsHtml("python/readme.md", "Python Documentation"), "text/html"));
            _app.MapGet("/docs/typescript", () => Results.Content(ServeMarkdownAsHtml("typescript/readme.md", "TypeScript Documentation"), "text/html"));
            _app.MapGet("/docs/go", () => Results.Content(ServeMarkdownAsHtml("go/readme.md", "Go Documentation"), "text/html"));
            _app.MapGet("/docs/javascript", () => Results.Content(ServeMarkdownAsHtml("javascript/readme.md", "JavaScript Documentation"), "text/html"));

            // Language-specific helper file endpoints
            _app.MapGet("/kotlin", () => Results.File(GetEmbeddedResourceBytes("helpers/AdapterHelpers.kt"), "text/plain", "AdapterHelpers.kt"));
            _app.MapGet("/swift", () => Results.File(GetEmbeddedResourceBytes("helpers/AdapterHelpers.swift"), "text/plain", "AdapterHelpers.swift"));
            _app.MapGet("/java", () => Results.File(GetEmbeddedResourceBytes("helpers/TrackableDTO.java"), "text/plain", "TrackableDTO.java"));
            _app.MapGet("/typescript", () => Results.File(GetEmbeddedResourceBytes("helpers/decorators.ts"), "text/plain", "decorators.ts"));
            _app.MapGet("/javascript", () => Results.File(GetEmbeddedResourceBytes("helpers/helpers.js"), "text/plain", "helpers.js"));
            _app.MapGet("/go", () => Results.File(GetEmbeddedResourceBytes("helpers/tracking.go"), "text/plain", "tracking.go"));

            #endregion

            _logger.LogInformation("📚 Documentation and helper server starting on {Url}", url);
            await _app.StartAsync(cancellationToken);
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

        private string GetLanguageNavigationHtml()
        {
            // In a real application, this might be a razor page or a more robust template.
            return @"
<!DOCTYPE html>
<html>
<head><title>Mobile Adapter Generator - Docs</title></head>
<body>
    <h1>Mobile Adapter Generator Documentation</h1>
    <p>Select your source language for detailed instructions and examples.</p>
    <ul>
        <li><a href='/docs/csharp'>C#</a></li>
        <li><a href='/docs/java'>Java</a></li>
        <li><a href='/docs/python'>Python</a></li>
        <li><a href='/docs/typescript'>TypeScript</a></li>
        <li><a href='/docs/javascript'>JavaScript</a></li>
        <li><a href='/docs/go'>Go</a></li>
    </ul>
    <h2>Helper Files</h2>
    <p>Download helper files for your project:</p>
    <ul>
        <li><a href='/kotlin'>Kotlin Helpers (for Android)</a></li>
        <li><a href='/swift'>Swift Helpers (for iOS)</a></li>
        <li><a href='/java'>Java @TrackableDTO Annotation</a></li>
        <li><a href='/typescript'>TypeScript @TrackableDTO Decorator</a></li>
        <li><a href='/javascript'>JavaScript Helper for JSDoc</a></li>
        <li><a href='/go'>Go Tracking Comment Helper</a></li>
    </ul>
</body>
</html>";
        }

        private string ServeMarkdownAsHtml(string resourceName, string title)
        {
            var markdown = GetEmbeddedResourceContent(resourceName);
            return ConvertMarkdownToHtml(markdown, title);
        }

        private byte[] GetEmbeddedResourceBytes(string resourceName)
        {
            var content = GetEmbeddedResourceContent(resourceName);
            return Encoding.UTF8.GetBytes(content);
        }

        private string GetEmbeddedResourceContent(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullResourceName = $"x3squaredcircles.MobileAdapter.Generator.Docs.{resourceName.Replace("/", ".")}";

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
            content.Replace("&#13;&#10;", "<br/>"); // Basic line breaks
            content.Replace("&#10;", "<br/>");

            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <title>{HtmlEncoder.Default.Encode(title)}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; padding: 2em; max-width: 800px; margin: 0 auto; }}
        pre {{ background-color: #f6f8fa; padding: 16px; overflow: auto; border-radius: 3px; white-space: pre-wrap; }}
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