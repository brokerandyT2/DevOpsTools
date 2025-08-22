using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface ILicenseClientService
    {
        Task<LicenseSession?> AcquireLicenseAsync(TokensConfiguration config);
        Task ReleaseLicenseAsync(LicenseSession session);
        Task StartHeartbeatAsync(LicenseSession session, CancellationToken cancellationToken);
    }

    public class LicenseClientService : ILicenseClientService
    {
        private readonly HttpClient _httpClient;
        private readonly IAppLogger _logger;
        private string _licenseServerUrl = string.Empty;

        public LicenseClientService(HttpClient httpClient, IAppLogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<LicenseSession?> AcquireLicenseAsync(TokensConfiguration config)
        {
            _licenseServerUrl = config.License.ServerUrl;
            _logger.LogInfo($"Acquiring license from: {_licenseServerUrl}");

            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(config.License.TimeoutSeconds);
            var retryInterval = TimeSpan.FromSeconds(30);

            while (DateTime.UtcNow - startTime < timeout)
            {
                try
                {

                    var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                    var toolName = "3SC-DesignToken-Generator";
                    var toolVersion = assembly.GetName().Version?.ToString(3) ?? "1.0.0";
                    var request = new LicenseAcquireRequest
                    {
                        ToolName = toolName,
                        ToolVersion = toolVersion,
                        IpAddress = await GetLocalIpAddressAsync(),
                        BuildId = GetBuildId()
                    };
                    var response = await SendLicenseRequestAsync<LicenseAcquireResponse>("license/acquire", request);

                    if (response?.LicenseGranted == true)
                    {
                        var session = new LicenseSession { SessionId = response.SessionId ?? string.Empty, BurstMode = response.BurstMode };
                        _logger.LogInfo(response.BurstMode ? "⚠️ License GRANTED IN BURST MODE ⚠️" : "✓ License acquired successfully");
                        _logger.LogDebug($"Session ID: {session.SessionId}");
                        return session;
                    }

                    if (response != null)
                    {
                        _logger.LogWarning($"License denied: {response.Reason ?? "unknown reason"}");
                        var retryAfter = response.RetryAfterSeconds > 0 ? response.RetryAfterSeconds : retryInterval.TotalSeconds;
                        _logger.LogInfo($"Retrying in {retryAfter} seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(retryAfter));
                        continue;
                    }

                    _logger.LogWarning($"License request failed. Retrying in {retryInterval.TotalSeconds} seconds...");
                    await Task.Delay(retryInterval);
                }
                catch (Exception ex)
                {
                    _logger.LogError("An error occurred during license acquisition. Retrying...", ex);
                    await Task.Delay(retryInterval);
                }
            }

            throw new DesignTokenException(DesignTokenExitCode.LicenseUnavailable, $"Failed to acquire license within {config.License.TimeoutSeconds} seconds");
        }

        public async Task ReleaseLicenseAsync(LicenseSession session)
        {
            if (string.IsNullOrEmpty(session.SessionId)) return;
            try
            {
                _logger.LogInfo($"Releasing license session: {session.SessionId}");
                session.HeartbeatTimer?.Dispose();
                var request = new { SessionId = session.SessionId };
                await SendLicenseRequestAsync<object>("license/release", request);
                _logger.LogInfo("✓ License released successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to release license (non-critical): {ex.Message}");
            }
        }

        public async Task StartHeartbeatAsync(LicenseSession session, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(session.SessionId)) return;
            _logger.LogDebug($"Starting license heartbeat for session: {session.SessionId}");

            var heartbeatInterval = TimeSpan.FromMinutes(2);
            session.HeartbeatTimer = new Timer(async _ =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    session.HeartbeatTimer?.Dispose();
                    return;
                }
                try
                {
                    var request = new { SessionId = session.SessionId };
                    await SendLicenseRequestAsync<object>("license/heartbeat", request);
                    _logger.LogDebug($"License heartbeat sent: {session.SessionId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"License heartbeat failed: {ex.Message}");
                }
            }, null, heartbeatInterval, heartbeatInterval);

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        private async Task<T?> SendLicenseRequestAsync<T>(string endpoint, object request) where T : class
        {
            var url = $"{_licenseServerUrl.TrimEnd('/')}/{endpoint}";
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"License request to '{url}' failed: {(int)response.StatusCode}");
                return null;
            }
            if (string.IsNullOrWhiteSpace(responseContent) || typeof(T) == typeof(object)) return default;

            return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private async Task<string> GetLocalIpAddressAsync()
        {
            try
            {
                var host = await Dns.GetHostEntryAsync(Dns.GetHostName());
                return host.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
            }
            catch { return "unknown-ip"; }
        }

        private string GetBuildId()
        {
            return Environment.GetEnvironmentVariable("BUILD_BUILDID") ??
                   Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ??
                   Environment.GetEnvironmentVariable("CI_PIPELINE_ID") ??
                   Environment.GetEnvironmentVariable("BUILD_NUMBER") ??
                   Guid.NewGuid().ToString();
        }

        private class LicenseAcquireRequest
        {
            public string ToolName { get; set; } = "design-token-generator";
            public string ToolVersion { get; set; } = "1.0.0";
            public string IpAddress { get; set; } = string.Empty;
            public string BuildId { get; set; } = string.Empty;
        }

        private class LicenseAcquireResponse
        {
            public bool LicenseGranted { get; set; }
            public string? SessionId { get; set; }
            public bool BurstMode { get; set; }
            public string? Reason { get; set; }
            public int RetryAfterSeconds { get; set; }
        }
    }
}