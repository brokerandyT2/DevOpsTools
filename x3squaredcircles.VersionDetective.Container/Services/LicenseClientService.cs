using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container.Services
{
    public interface ILicenseClientService
    {
        Task<LicenseSession?> AcquireLicenseAsync(VersionDetectiveConfiguration config);
        Task ReleaseLicenseAsync(LicenseSession session);
        Task StartHeartbeatAsync(LicenseSession session, CancellationToken cancellationToken);
    }

    public class LicenseSession
    {
        public string SessionId { get; set; } = string.Empty;
        public bool BurstMode { get; set; }
        public int BurstCountRemaining { get; set; }
        public DateTime ExpiresAt { get; set; }
        public Timer? HeartbeatTimer { get; set; }
    }

    public class LicenseClientService : ILicenseClientService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<LicenseClientService> _logger;

        public LicenseClientService(HttpClient httpClient, ILogger<LicenseClientService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<LicenseSession?> AcquireLicenseAsync(VersionDetectiveConfiguration config)
        {
            _logger.LogInformation("Acquiring license from: {LicenseServer}", config.License.ServerUrl);

            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(config.License.TimeoutSeconds);
            var retryInterval = TimeSpan.FromSeconds(config.License.RetryIntervalSeconds);

            while (DateTime.UtcNow - startTime < timeout)
            {
                try
                {
                    var request = new LicenseAcquireRequest
                    {
                        ToolName = config.License.ToolName,
                        ToolVersion = "1.0.0", // Could be made configurable
                        IpAddress = await GetLocalIpAddressAsync(),
                        BuildId = GetBuildId()
                    };

                    var response = await SendLicenseRequestAsync<LicenseAcquireResponse>(
                        config.License.ServerUrl, "license/acquire", request);

                    if (response != null && response.LicenseGranted)
                    {
                        var session = new LicenseSession
                        {
                            SessionId = response.SessionId ?? string.Empty,
                            BurstMode = response.BurstMode,
                            BurstCountRemaining = response.BurstCountRemaining,
                            ExpiresAt = response.ExpiresAt ?? DateTime.UtcNow.AddMinutes(5)
                        };

                        if (response.BurstMode)
                        {
                            _logger.LogWarning("⚠️ LICENSE GRANTED IN BURST MODE ⚠️");
                            _logger.LogWarning("Session: {SessionId}", session.SessionId);
                            _logger.LogWarning("Burst events remaining this month: {Remaining}", response.BurstCountRemaining);
                            _logger.LogWarning("Consider purchasing additional licenses to avoid future interruptions");
                        }
                        else
                        {
                            _logger.LogInformation("✓ License acquired successfully");
                            _logger.LogInformation("Session: {SessionId}", session.SessionId);
                        }

                        return session;
                    }

                    if (response != null)
                    {
                        _logger.LogWarning("License denied: {Reason}", response.Reason ?? "unknown");

                        if (response.Reason == "concurrent_limit_exceeded")
                        {
                            if (response.BurstEventsExhausted)
                            {
                                _logger.LogError("Burst capacity exhausted for this month. No more licenses available.");
                                throw new VersionDetectiveException(VersionDetectiveExitCode.LicenseUnavailable,
                                    "Burst capacity exhausted - no licenses available this month");
                            }

                            _logger.LogInformation("Concurrent limit exceeded. Retrying in {RetryAfter} seconds...", response.RetryAfterSeconds);
                            await Task.Delay(TimeSpan.FromSeconds(response.RetryAfterSeconds));
                            continue;
                        }

                        if (response.Reason == "tool_not_licensed")
                        {
                            throw new VersionDetectiveException(VersionDetectiveExitCode.LicenseUnavailable,
                                $"Tool '{config.License.ToolName}' is not licensed on this server");
                        }
                    }

                    _logger.LogWarning("License request failed. Retrying in {RetryInterval} seconds...", retryInterval.TotalSeconds);
                    await Task.Delay(retryInterval);
                }
                catch (VersionDetectiveException)
                {
                    throw; // Re-throw our custom exceptions
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("License server unreachable: {Error}. Retrying in {RetryInterval} seconds...",
                        ex.Message, retryInterval.TotalSeconds);
                    await Task.Delay(retryInterval);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    _logger.LogWarning("License server timeout. Retrying in {RetryInterval} seconds...", retryInterval.TotalSeconds);
                    await Task.Delay(retryInterval);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error during license acquisition");
                    await Task.Delay(retryInterval);
                }
            }

            _logger.LogError("License acquisition timeout after {Timeout} seconds", config.License.TimeoutSeconds);
            throw new VersionDetectiveException(VersionDetectiveExitCode.LicenseUnavailable,
                $"Failed to acquire license within {config.License.TimeoutSeconds} seconds");
        }

        public async Task ReleaseLicenseAsync(LicenseSession session)
        {
            try
            {
                _logger.LogInformation("Releasing license session: {SessionId}", session.SessionId);

                // Stop heartbeat timer
                session.HeartbeatTimer?.Dispose();

                // Don't fail the pipeline if license release fails
                var licenseServer = GetLicenseServerFromSession(session);
                if (string.IsNullOrEmpty(licenseServer))
                {
                    _logger.LogWarning("Cannot determine license server for session release");
                    return;
                }

                var request = new LicenseReleaseRequest
                {
                    SessionId = session.SessionId
                };

                await SendLicenseRequestAsync<object>(licenseServer, "license/release", request);
                _logger.LogInformation("✓ License released successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release license (non-critical): {SessionId}", session.SessionId);
                // Don't throw - license release failures shouldn't fail the pipeline
            }
        }

        public async Task StartHeartbeatAsync(LicenseSession session, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting license heartbeat for session: {SessionId}", session.SessionId);

            try
            {
                var licenseServer = GetLicenseServerFromSession(session);
                if (string.IsNullOrEmpty(licenseServer))
                {
                    _logger.LogWarning("Cannot determine license server for heartbeat");
                    return;
                }

                // Send heartbeat every 2 minutes (license timeout is typically 5 minutes)
                var heartbeatInterval = TimeSpan.FromMinutes(2);

                session.HeartbeatTimer = new Timer(async _ =>
                {
                    try
                    {
                        var request = new LicenseHeartbeatRequest
                        {
                            SessionId = session.SessionId
                        };

                        await SendLicenseRequestAsync<object>(licenseServer, "license/heartbeat", request);
                        _logger.LogDebug("License heartbeat sent: {SessionId}", session.SessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "License heartbeat failed: {SessionId}", session.SessionId);
                    }
                }, null, heartbeatInterval, heartbeatInterval);

                // Keep heartbeat running until cancellation
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("License heartbeat cancelled for session: {SessionId}", session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "License heartbeat error for session: {SessionId}", session.SessionId);
            }
            finally
            {
                session.HeartbeatTimer?.Dispose();
            }
        }

        private async Task<T?> SendLicenseRequestAsync<T>(string licenseServer, string endpoint, object request) where T : class
        {
            var url = $"{licenseServer.TrimEnd('/')}/{endpoint}";
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug("Sending license request to: {Url}", url);

            using var httpResponse = await _httpClient.PostAsync(url, content);

            var responseContent = await httpResponse.Content.ReadAsStringAsync();

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("License request failed: {StatusCode} - {Content}",
                    httpResponse.StatusCode, responseContent);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Try to parse retry-after header
                    if (httpResponse.Headers.RetryAfter?.Delta.HasValue == true)
                    {
                        var retryAfter = (int)httpResponse.Headers.RetryAfter.Delta.Value.TotalSeconds;
                        return JsonSerializer.Deserialize<T>($"{{\"retryAfterSeconds\":{retryAfter}}}");
                    }
                }

                return null;
            }

            if (typeof(T) == typeof(object))
            {
                return default(T); // For void responses
            }

            try
            {
                return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize license response: {Content}", responseContent);
                return null;
            }
        }

        private async Task<string> GetLocalIpAddressAsync()
        {
            try
            {
                // Simple method to get local IP
                var hostName = Dns.GetHostName();
                var addresses = await Dns.GetHostAddressesAsync(hostName);

                foreach (var address in addresses)
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address))
                    {
                        return address.ToString();
                    }
                }

                return "127.0.0.1";
            }
            catch
            {
                return "unknown";
            }
        }

        private string GetBuildId()
        {
            // Try to get build ID from various CI/CD environments
            return Environment.GetEnvironmentVariable("BUILD_BUILDID") ??           // Azure DevOps
                   Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ??           // GitHub Actions
                   Environment.GetEnvironmentVariable("CI_PIPELINE_ID") ??          // GitLab CI
                   Environment.GetEnvironmentVariable("BUILD_NUMBER") ??            // Jenkins
                   Environment.GetEnvironmentVariable("BUILDKITE_BUILD_NUMBER") ??  // Buildkite
                   DateTime.UtcNow.ToString("yyyyMMddHHmmss");                     // Fallback
        }

        private string GetLicenseServerFromSession(LicenseSession session)
        {
            // In a real implementation, we might store the license server URL with the session
            // For now, try to get it from environment
            return Environment.GetEnvironmentVariable("LICENSE_SERVER") ?? string.Empty;
        }
    }
}