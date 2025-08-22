using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using x3squaredcircles.runner.container.Api;
using x3squaredcircles.runner.container.Engine;

namespace x3squaredcircles.runner.container;

/// <summary>
/// The main background service for The Conductor.
/// This class is the primary entry point after the host is configured. It is responsible for
/// orchestrating the startup and shutdown of the CoreEngine and the ApiService.
/// </summary>
public class ConductorService : BackgroundService
{
    private readonly ILogger<ConductorService> _logger;
    private readonly CoreEngine _coreEngine;
    private readonly IApiService _apiService;
    private readonly IHostApplicationLifetime _lifetime;

    public ConductorService(
        ILogger<ConductorService> logger,
        CoreEngine coreEngine,
        IApiService apiService,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _coreEngine = coreEngine;
        _apiService = apiService;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("--- 3SC Conductor Service is starting up. ---");

        try
        {
            // Start the API service in the background. It will run until the stoppingToken is cancelled.
            var apiTask = _apiService.StartAsync(stoppingToken);

            // Start the Core Engine in the foreground. This task represents the main application logic.
            // If it exits for any reason (fatal error, etc.), the service will stop.
            var engineTask = _coreEngine.StartAsync(stoppingToken);

            // Wait for either the engine to complete its task (e.g., due to an unhandled error)
            // or for the API to complete its task. In a healthy shutdown, the stoppingToken
            // will cause both to complete gracefully.
            await Task.WhenAny(engineTask, apiTask);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "A critical, unhandled exception occurred in the main Conductor service. The application will now terminate.");
            // In case of a catastrophic failure, ensure the application shuts down.
            _lifetime.StopApplication();
        }

        _logger.LogInformation("--- 3SC Conductor Service is shutting down. ---");
    }
}