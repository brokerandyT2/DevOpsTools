using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IHttpServerService
    {
        Task StartHttpServerAsync(CancellationToken cancellationToken);
    }

    public class HttpServerService : IHttpServerService
    {
        private readonly ILogger<HttpServerService> _logger;
        private HttpListener? _httpListener;
        private readonly int _port = 8080;

        public HttpServerService(ILogger<HttpServerService> logger)
        {
            _logger = logger;
        }

        public async Task StartHttpServerAsync(CancellationToken cancellationToken)
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://+:{_port}/");
                _httpListener.Start();

                _logger.LogInformation("HTTP server started on port {Port}", _port);

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
                    }
                    catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error accepting HTTP request");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start HTTP server");
            }
            finally
            {
                try
                {
                    _httpListener?.Stop();
                    _httpListener?.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping HTTP listener");
                }

                _logger.LogInformation("HTTP server stopped");
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                _logger.LogDebug("HTTP Request: {Method} {Url}", request.HttpMethod, request.Url?.PathAndQuery);

                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                if (request.HttpMethod != "GET")
                {
                    response.StatusCode = 405;
                    response.ContentType = "text/plain";
                    var errorBytes = Encoding.UTF8.GetBytes("Method Not Allowed");
                    await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                    response.Close();
                    return;
                }

                var path = request.Url?.AbsolutePath?.ToLowerInvariant() ?? "";

                switch (path)
                {
                    case "/docs":
                    case "/docs/":
                        await HandleDocsRequestAsync(response);
                        break;

                    case "/health":
                    case "/healthz":
                        await HandleHealthRequestAsync(response);
                        break;

                    case "/status":
                        await HandleStatusRequestAsync(response);
                        break;

                    default:
                        await HandleNotFoundRequestAsync(response);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling HTTP request");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch
                {
                }
            }
        }

        private async Task HandleDocsRequestAsync(HttpListenerResponse response)
        {
            try
            {
                var htmlContent = GetDocumentationHtml();
                var contentBytes = Encoding.UTF8.GetBytes(htmlContent);

                response.StatusCode = 200;
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = contentBytes.Length;

                await response.OutputStream.WriteAsync(contentBytes, 0, contentBytes.Length);
                response.Close();

                _logger.LogDebug("Served documentation to client");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving documentation");
                response.StatusCode = 500;
                response.Close();
            }
        }

        private async Task HandleHealthRequestAsync(HttpListenerResponse response)
        {
            try
            {
                var healthData = new
                {
                    status = "healthy",
                    service = "sql-schema-generator",
                    timestamp = DateTime.UtcNow.ToString("O")
                };

                var jsonContent = JsonSerializer.Serialize(healthData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });

                var contentBytes = Encoding.UTF8.GetBytes(jsonContent);

                response.StatusCode = 200;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = contentBytes.Length;

                await response.OutputStream.WriteAsync(contentBytes, 0, contentBytes.Length);
                response.Close();

                _logger.LogDebug("Served health check to client");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving health check");
                response.StatusCode = 500;
                response.Close();
            }
        }

        private async Task HandleStatusRequestAsync(HttpListenerResponse response)
        {
            try
            {
                var statusData = new
                {
                    service = "sql-schema-generator",
                    version = "1.0.0",
                    status = "running",
                    timestamp = DateTime.UtcNow.ToString("O")
                };

                var jsonContent = JsonSerializer.Serialize(statusData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });

                var contentBytes = Encoding.UTF8.GetBytes(jsonContent);

                response.StatusCode = 200;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = contentBytes.Length;

                await response.OutputStream.WriteAsync(contentBytes, 0, contentBytes.Length);
                response.Close();

                _logger.LogDebug("Served status to client");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving status");
                response.StatusCode = 500;
                response.Close();
            }
        }

        private async Task HandleNotFoundRequestAsync(HttpListenerResponse response)
        {
            try
            {
                var notFoundHtml = CreateErrorHtml(
                    "404 - Not Found",
                    "The requested resource was not found. Available endpoints: /docs, /health, /status"
                );

                var contentBytes = Encoding.UTF8.GetBytes(notFoundHtml);

                response.StatusCode = 404;
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = contentBytes.Length;

                await response.OutputStream.WriteAsync(contentBytes, 0, contentBytes.Length);
                response.Close();

                _logger.LogDebug("Served 404 response to client");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving 404 response");
                response.StatusCode = 500;
                response.Close();
            }
        }

        private string GetDocumentationHtml()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "x3squaredcircles.SQLSync.Generator.Docs.readme.md";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _logger.LogWarning("Documentation resource not found: {ResourceName}", resourceName);
                    return CreateErrorHtml("Documentation not found", "The embedded documentation resource could not be loaded.");
                }

                using var reader = new StreamReader(stream);
                var markdownContent = reader.ReadToEnd();

                return ConvertMarkdownToHtml(markdownContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load documentation");
                return CreateErrorHtml("Documentation Error", $"Failed to load documentation: {ex.Message}");
            }
        }

        private string ConvertMarkdownToHtml(string markdown)
        {
            var html = new StringBuilder();
            var lines = markdown.Split('\n');
            bool inCodeBlock = false;
            bool inList = false;

            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine("    <title>SQL Schema Generator - Documentation</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; margin: 0; padding: 20px; max-width: 1200px; margin: 0 auto; }");
            html.AppendLine("        h1, h2, h3 { color: #333; border-bottom: 1px solid #eee; padding-bottom: 0.3em; }");
            html.AppendLine("        h1 { font-size: 2em; }");
            html.AppendLine("        h2 { font-size: 1.5em; margin-top: 2em; }");
            html.AppendLine("        h3 { font-size: 1.2em; margin-top: 1.5em; }");
            html.AppendLine("        code { background: #f6f8fa; padding: 2px 4px; border-radius: 3px; font-family: 'SFMono-Regular', Consolas, monospace; }");
            html.AppendLine("        pre { background: #f6f8fa; padding: 16px; border-radius: 6px; overflow-x: auto; }");
            html.AppendLine("        pre code { background: none; padding: 0; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            foreach (var line in lines)
            {
                var trimmedLine = line.TrimEnd();

                if (trimmedLine.StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        html.AppendLine("</code></pre>");
                        inCodeBlock = false;
                    }
                    else
                    {
                        html.AppendLine("<pre><code>");
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    html.AppendLine(HtmlEncoder.Default.Encode(trimmedLine));
                    continue;
                }

                if (trimmedLine.StartsWith("# "))
                {
                    var headerText = trimmedLine[2..];
                    html.AppendLine($"<h1>{HtmlEncoder.Default.Encode(headerText)}</h1>");
                }
                else if (trimmedLine.StartsWith("## "))
                {
                    var headerText = trimmedLine[3..];
                    html.AppendLine($"<h2>{HtmlEncoder.Default.Encode(headerText)}</h2>");
                }
                else if (trimmedLine.StartsWith("### "))
                {
                    var headerText = trimmedLine[4..];
                    html.AppendLine($"<h3>{HtmlEncoder.Default.Encode(headerText)}</h3>");
                }
                else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
                {
                    if (!inList)
                    {
                        html.AppendLine("<ul>");
                        inList = true;
                    }
                    var listItem = trimmedLine[2..];
                    html.AppendLine($"<li>{HtmlEncoder.Default.Encode(listItem)}</li>");
                }
                else if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    if (inList)
                    {
                        html.AppendLine("</ul>");
                        inList = false;
                    }
                    html.AppendLine("<br>");
                }
                else
                {
                    if (inList)
                    {
                        html.AppendLine("</ul>");
                        inList = false;
                    }
                    html.AppendLine($"<p>{HtmlEncoder.Default.Encode(trimmedLine)}</p>");
                }
            }

            if (inList)
            {
                html.AppendLine("</ul>");
            }

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }

        private string CreateErrorHtml(string title, string message)
        {
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{HtmlEncoder.Default.Encode(title)}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; 
               margin: 0; padding: 40px; text-align: center; color: #333; }}
        .error-container {{ max-width: 600px; margin: 0 auto; }}
        h1 {{ color: #d73a49; }}
        p {{ font-size: 1.1em; line-height: 1.6; }}
        .support {{ margin-top: 40px; padding: 20px; background: #f6f8fa; border-radius: 6px; }}
    </style>
</head>
<body>
    <div class=""error-container"">
        <h1>{HtmlEncoder.Default.Encode(title)}</h1>
        <p>{HtmlEncoder.Default.Encode(message)}</p>
        <div class=""support"">
            <p><strong>Available Endpoints:</strong></p>
            <p><code>/docs</code> - Documentation<br>
               <code>/health</code> - Health Check<br>
               <code>/status</code> - Status JSON</p>
        </div>
    </div>
</body>
</html>";
        }
    }
}