using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.datalink.container.Models;

namespace x3squaredcircles.datalink.container.Services
{
    public class ControlPointService : IControlPointService
    {
        private readonly DataLinkConfiguration _config;
        private readonly IAppLogger _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _toolVersion;

        public ControlPointService(DataLinkConfiguration config, IAppLogger logger, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "6.0.0";
        }

        public async Task InvokeNotificationAsync(ControlPointType type, string command, string? errorMessage = null)
        {
            var url = GetUrlForNotificationType(type);
            if (string.IsNullOrEmpty(url))
            {
                _logger.LogDebug($"Notification Control Point '{type}' is not configured. Skipping invocation.");
                return;
            }

            _logger.LogInfo($"Invoking Notification Control Point '{type}' for command '{command}'...");

            var payload = new ControlPointPayload
            {
                EventType = type.ToString(),
                Command = command,
                Status = type == ControlPointType.OnFailure ? "Failure" : "Success",
                ErrorMessage = errorMessage,
                ToolVersion = _toolVersion,
                TimestampUtc = DateTime.UtcNow.ToString("O")
            };

            await SendRequestAsync(url, payload, type.ToString());
        }

        public async Task<bool> InvokeDeploymentOverrideAsync(DeploymentOverridePayload payload)
        {
            var url = _config.ControlPointDeploymentOverrideUrl;
            if (string.IsNullOrEmpty(url))
            {
                // This should not happen if called correctly from the orchestrator, but is a safe guard.
                _logger.LogDebug("Deployment Override Control Point is not configured.");
                return false;
            }

            _logger.LogInfo("Invoking Deployment Override Control Point...");

            return await SendRequestAsync(url, payload, ControlPointType.DeploymentOverride.ToString());
        }

        private string? GetUrlForNotificationType(ControlPointType type)
        {
            return type switch
            {
                ControlPointType.OnStartup => _config.ControlPointOnStartupUrl,
                ControlPointType.OnSuccess => _config.ControlPointOnSuccessUrl,
                ControlPointType.OnFailure => _config.ControlPointOnFailureUrl,
                _ => null
            };
        }

        private async Task<bool> SendRequestAsync(string url, object payload, string controlPointName)
        {
            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var client = _httpClientFactory.CreateClient("ControlPointClient");
                client.Timeout = TimeSpan.FromSeconds(_config.ControlPointTimeoutSeconds);

                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInfo($"✓ Control Point '{controlPointName}' invoked successfully (Status: {(int)response.StatusCode}).");
                    return true;
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning($"Control Point '{controlPointName}' invocation failed with status code {(int)response.StatusCode}. Response: {responseBody}");
                    HandleFailure($"Control Point returned non-success status code: {(int)response.StatusCode}");
                    return false;
                }
            }
            catch (TaskCanceledException) // Catches timeouts
            {
                _logger.LogError($"Control Point '{controlPointName}' invocation timed out after {_config.ControlPointTimeoutSeconds} seconds.");
                HandleFailure("Control Point invocation timed out.");
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Control Point '{controlPointName}' invocation failed with a network error: {ex.Message}");
                HandleFailure($"Control Point network error: {ex.Message}");
                return false;
            }
        }

        private void HandleFailure(string reason)
        {
            if (_config.ControlPointTimeoutAction == "fail")
            {
                throw new DataLinkException(ExitCode.UnhandledException, "CONTROL_POINT_FAILURE", $"Failing build because Control Point failed: {reason}");
            }
            _logger.LogWarning("Continuing execution despite Control Point failure as per configuration ('continue').");
        }
    }
}