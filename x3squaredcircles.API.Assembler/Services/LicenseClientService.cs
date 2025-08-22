using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for the license management service.
    /// </summary>
    public interface ILicenseClientService
    {
        /// <summary>
        /// Acquires a license from the central server for the current tool execution.
        /// </summary>
        /// <returns>A session object representing the acquired license.</returns>
        Task<object> AcquireLicenseAsync();

        /// <summary>
        /// Releases a previously acquired license session.
        /// </summary>
        /// <param name="session">The session object returned by AcquireLicenseAsync.</param>
        Task ReleaseLicenseAsync(object session);
    }

    /// <summary>
    /// Manages the acquisition and release of licenses from the central 3SC Licensing Server.
    /// This service respects the NO_OP flag for dry runs and includes resilient retry logic.
    /// </summary>
    public class LicenseClientService : ILicenseClientService
    {
        private readonly ILogger<LicenseClientService> _logger;
        private readonly HttpClient _httpClient;
        private readonly AssemblerConfiguration _config;
        private static readonly object _dummySession = new object(); // Pre-allocated dummy session for NO_OP

        public LicenseClientService(ILogger<LicenseClientService> logger, IHttpClientFactory httpClientFactory, AssemblerConfiguration config)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("LicenseClient");
            _config = config;
        }

        public async Task<object> AcquireLicenseAsync()
        {
            // If in NO_OP mode, immediately return a success object without hitting the network.
            if (_config.NoOp)
            {
                _logger.LogInformation("NO_OP mode enabled. Skipping license acquisition.");
                return _dummySession;
            }

            if (string.IsNullOrWhiteSpace(_config.License.ServerUrl))
            {
                _logger.LogError("Required environment variable LICENSE_SERVER (or 3SC_LICENSE_SERVER) is not configured.");
                throw new AssemblerException(AssemblerExitCode.InvalidConfiguration, "License server URL is not configured.");
            }

            _logger.LogInformation("Acquiring license from: {LicenseServer}", _config.License.ServerUrl);

            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(_config.License.TimeoutSeconds);
            var retryInterval = TimeSpan.FromSeconds(_config.License.RetryIntervalSeconds);

            var request = new
            {
                toolName = Assembly.GetExecutingAssembly().GetName().Name,
                toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                repoUrl = _config.RepoUrl,
                branch = _config.Branch
            };
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            while (DateTime.UtcNow - startTime < timeout)
            {
                try
                {
                    // Use a CancellationToken for the individual request that respects the retry interval
                    using var cts = new CancellationTokenSource(retryInterval);
                    var response = await _httpClient.PostAsync($"{_config.License.ServerUrl}/api/license/acquire", content, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        var session = JsonSerializer.Deserialize<object>(responseBody);
                        _logger.LogInformation("✓ License acquired successfully.");
                        return session ?? _dummySession;
                    }

                    _logger.LogWarning("License server returned status {StatusCode}. Retrying in {Seconds}s...", response.StatusCode, retryInterval.TotalSeconds);
                }
                catch (TaskCanceledException)
                {
                    // This is an expected timeout for a single attempt, just log and continue the loop.
                    _logger.LogDebug("License acquisition attempt timed out. Retrying...");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to license server. Retrying in {Seconds}s...", retryInterval.TotalSeconds);
                }

                // Wait for the retry interval before the next attempt in the while loop
                await Task.Delay(retryInterval);
            }

            throw new AssemblerException(AssemblerExitCode.LicenseUnavailable, $"Failed to acquire license from '{_config.License.ServerUrl}' after {timeout.TotalSeconds} seconds.");
        }

        public async Task ReleaseLicenseAsync(object session)
        {
            // Do not attempt to release if we were in NO_OP mode or the session is invalid.
            if (_config.NoOp || session == null || session == _dummySession || string.IsNullOrWhiteSpace(_config.License.ServerUrl))
            {
                return;
            }

            try
            {
                _logger.LogInformation("Releasing license...");
                var content = new StringContent(JsonSerializer.Serialize(session), Encoding.UTF8, "application/json");

                // Use a short timeout for the release operation as it's non-critical.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var response = await _httpClient.PostAsync($"{_config.License.ServerUrl}/api/license/release", content, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✓ License released successfully.");
                }
                else
                {
                    _logger.LogWarning("Failed to release license. Server responded with {StatusCode}.", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                // A failure to release should NEVER fail the pipeline. Log it as an error and move on.
                _logger.LogError(ex, "An error occurred while releasing the license. This will not fail the operation.");
            }
        }
    }
}