using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public class ControlPointService : IControlPointService
    {
        private readonly TokensConfiguration _config;
        private readonly IAppLogger _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _toolVersion;

        public ControlPointService(TokensConfiguration config, IAppLogger logger, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        }

        public async Task<bool> InvokeAsync(ControlPointStage stage, string eventName, bool isBlocking = false, Dictionary<string, object>? payloadMetadata = null)
        {
            var url = GetUrlForStage(stage, eventName);
            if (string.IsNullOrEmpty(url))
            {
                _logger.LogDebug($"Control Point for stage '{stage}' event '{eventName}' is not configured. Skipping.");
                return true; // If not configured, it's a "success"
            }

            var status = eventName.Contains("Failure") ? "Failure" : "Success";
            var errorMessage = (status == "Failure" && payloadMetadata?.ContainsKey("error") == true) ? payloadMetadata["error"].ToString() : null;

            _logger.LogInfo($"Invoking{(isBlocking ? " [BLOCKING]" : "")} Control Point: {stage}_{eventName}");

            var payload = new ControlPointPayload
            {
                EventType = $"{stage}_{eventName}",
                ToolVersion = _toolVersion,
                TimestampUtc = DateTime.UtcNow.ToString("O"),
                Status = status,
                ErrorMessage = errorMessage,
                Metadata = payloadMetadata
            };

            return await SendRequestAsync(url, payload, $"{stage}_{eventName}", isBlocking);
        }

        private string? GetUrlForStage(ControlPointStage stage, string eventName)
        {
            return (stage, eventName) switch
            {
                (ControlPointStage.RunStart, "OnRunStart") => _config.ControlPoints.OnRunStartUrl,
                (ControlPointStage.Extract, "OnSuccess") => _config.ControlPoints.OnExtractSuccessUrl,
                (ControlPointStage.Extract, "OnFailure") => _config.ControlPoints.OnExtractFailureUrl,
                (ControlPointStage.Generate, "OnSuccess") => _config.ControlPoints.OnGenerateSuccessUrl,
                (ControlPointStage.Generate, "OnFailure") => _config.ControlPoints.OnGenerateFailureUrl,
                (ControlPointStage.Commit, "BeforeCommit") => _config.ControlPoints.BeforeCommitUrl,
                (ControlPointStage.Commit, "OnSuccess") => _config.ControlPoints.OnCommitSuccessUrl,
                (ControlPointStage.RunEnd, "OnSuccess") => _config.ControlPoints.OnRunSuccessUrl,
                (ControlPointStage.RunEnd, "OnFailure") => _config.ControlPoints.OnRunFailureUrl,
                _ => null
            };
        }

        private async Task<bool> SendRequestAsync(string url, object payload, string controlPointName, bool isBlocking)
        {
            var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var client = _httpClientFactory.CreateClient("ControlPointClient");
                client.Timeout = TimeSpan.FromSeconds(_config.ControlPoints.TimeoutSeconds);

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
                    HandleFailure($"Control Point returned non-success status code: {(int)response.StatusCode}", isBlocking);
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogError($"Control Point '{controlPointName}' invocation timed out after {_config.ControlPoints.TimeoutSeconds} seconds.");
                HandleFailure("Control Point invocation timed out.", isBlocking);
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Control Point '{controlPointName}' invocation failed with a network error: {ex.Message}");
                HandleFailure($"Control Point network error: {ex.Message}", isBlocking);
                return false;
            }
        }

        private void HandleFailure(string reason, bool isBlocking)
        {
            if (isBlocking && _config.ControlPoints.TimeoutAction == "fail")
            {
                throw new DesignTokenException(DesignTokenExitCode.UnhandledException, $"Failing build because blocking Control Point failed: {reason}");
            }
            _logger.LogWarning("Continuing execution despite Control Point failure (non-blocking or action='continue').");
        }
    }
}