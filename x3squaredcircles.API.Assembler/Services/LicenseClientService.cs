using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.API.Assembler.Configuration;
using x3squaredcircles.API.Assembler.Models;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Defines the contract for the license management service.
    /// </summary>
    public interface ILicenseClientService
    {
        Task<object> AcquireLicenseAsync();
        Task ReleaseLicenseAsync(object session);
    }

    /// <summary>
    /// Manages the acquisition and release of licenses from the central 3SC Licensing Server.
    /// </summary>
    public class LicenseClientService : ILicenseClientService
    {
        private readonly ILogger<LicenseClientService> _logger;
        private readonly HttpClient _httpClient;
        private readonly AssemblerConfiguration _config;

        public LicenseClientService(ILogger<LicenseClientService> logger, IHttpClientFactory httpClientFactory, AssemblerConfiguration config)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("LicenseClient");
            _config = config;
        }

        public async Task<object> AcquireLicenseAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.License.ServerUrl))
            {
                _logger.LogWarning("LICENSE_SERVER is not configured. Skipping license check (development mode).");
                return new object(); // Return a dummy session object
            }

            _logger.LogInformation("Acquiring license from: {LicenseServer}", _config.License.ServerUrl);

            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(_config.License.TimeoutSeconds);
            var retryInterval = TimeSpan.FromSeconds(15); // Fixed retry interval

            while (DateTime.UtcNow - startTime < timeout)
            {
                try
                {
                    var request = new { toolName = "3sc-api-assembler", repoUrl = _config.RepoUrl, branch = _config.Branch };
                    var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync($"{_config.License.ServerUrl}/api/license/acquire", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        var session = JsonSerializer.Deserialize<object>(responseBody); // Placeholder for a real session object
                        _logger.LogInformation("✓ License acquired successfully.");
                        return session;
                    }

                    _logger.LogWarning("License server returned status {StatusCode}. Retrying in {Seconds}s...", response.StatusCode, retryInterval.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to license server. Retrying in {Seconds}s...", retryInterval.TotalSeconds);
                }

                await Task.Delay(retryInterval);
            }

            throw new AssemblerException(AssemblerExitCode.LicenseUnavailable, $"Failed to acquire license from '{_config.License.ServerUrl}' after {timeout.TotalSeconds} seconds.");
        }

        public async Task ReleaseLicenseAsync(object session)
        {
            if (string.IsNullOrWhiteSpace(_config.License.ServerUrl) || session == null)
            {
                return; // No license to release
            }

            try
            {
                _logger.LogInformation("Releasing license...");
                var content = new StringContent(JsonSerializer.Serialize(session), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_config.License.ServerUrl}/api/license/release", content);
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
                // Do not fail the pipeline for a release failure.
                _logger.LogError(ex, "An error occurred while releasing the license. This will not fail the operation.");
            }
        }
    }
}