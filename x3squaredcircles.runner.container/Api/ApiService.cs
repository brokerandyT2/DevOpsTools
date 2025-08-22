using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using x3squaredcircles.runner.container.Engine;

namespace x3squaredcircles.runner.container.Api;

/// <summary>
/// Defines the contract for the API service that exposes control endpoints for The Conductor.
/// </summary>
public interface IApiService
{
    /// <summary>
    /// Asynchronously starts the web server to listen for API requests.
    /// This method will run until the application is shutting down.
    /// </summary>
    /// <param name="cancellationToken">A token to signal when the web server should shut down.</param>
    /// <returns>A task representing the asynchronous operation of the web server.</returns>
    Task StartAsync(CancellationToken cancellationToken);
}

public class ApiService : IApiService
{
    private readonly ILogger<ApiService> _logger;
    private readonly CoreEngine _coreEngine;
    private readonly WebApplication _app;

    public ApiService(ILogger<ApiService> logger, IHostApplicationLifetime lifetime, CoreEngine coreEngine)
    {
        _logger = logger;
        _coreEngine = coreEngine;

        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.ListenAnyIP(8080));

        _app = builder.Build();

        lifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogDebug("Main application host is stopping. Shutting down API service.");
            _app.StopAsync(CancellationToken.None).Wait();
        });

        // Map all API endpoints
        _app.MapPost("/refresh", HandleRefreshRequest);
        _app.MapGet("/docs", HandleDocsRequest);
    }

    private async Task HandleRefreshRequest(HttpContext context)
    {
        _logger.LogInformation("API: /refresh endpoint triggered by request.");

        await _coreEngine.RequestRefreshAsync();

        var response = new { status = "configuration_reload_triggered" };
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        await context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }

    private async Task HandleDocsRequest(HttpContext context)
    {
        _logger.LogDebug("API: /docs endpoint triggered by request.");

        var readmePath = Path.Combine("/src", "README.md");

        if (!File.Exists(readmePath))
        {
            _logger.LogWarning("Could not find README.md at path: {ReadmePath}", readmePath);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("README.md not found.", context.RequestAborted);
            return;
        }

        try
        {
            var readmeContent = await File.ReadAllTextAsync(readmePath, context.RequestAborted);
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(readmeContent, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while trying to read README.md.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync("An error occurred while serving the documentation.", context.RequestAborted);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("API service starting, listening on http://*:8080");
        try
        {
            await _app.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("API service is stopping.");
        }
    }
}