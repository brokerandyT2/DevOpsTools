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
    public class ControlPointService : IControlPointService
    {
        private readonly ILogger<ControlPointService> _logger;
        private readonly AssemblerConfiguration _config;
        private readonly HttpClient _httpClient;
        private static readonly string _toolName = Assembly.GetExecutingAssembly().GetName().Name ?? "3sc-api-assembler";
        private static readonly string _toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public ControlPointService(ILogger<ControlPointService> logger, AssemblerConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _config = config;
            _httpClient = httpClientFactory.CreateClient("ControlPointClient");
        }

        public async Task InvokeOnStartupAsync()
        {
            var endpoint = _config.ControlPoints.OnStartup;
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            var payload = new { configuration = _config };

            // OnStartup is a blocking call and must pass
            var response = await SendEventAsync(endpoint, "ON_STARTUP", payload, isBlocking: true);
            if (!response.IsSuccess)
            {
                throw new AssemblerException(AssemblerExitCode.UnhandledException, $"OnStartup Control Point failed: {response.ResponseMessage}");
            }
        }

        public async Task InvokeOnSuccessAsync()
        {
            var endpoint = _config.ControlPoints.OnSuccess;
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            var payload = new { result = new { status = "Success" } };

            // OnSuccess is fire-and-forget
            _ = SendEventAsync(endpoint, "ON_SUCCESS", payload, isBlocking: false);
        }

        public async Task InvokeOnFailureAsync(Exception ex)
        {
            var endpoint = _config.ControlPoints.OnFailure;
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            var payload = new
            {
                error = new
                {
                    message = ex.Message,
                    stackTrace = ex.ToString(),
                    exitCode = (ex is AssemblerException ae) ? ae.ExitCode.ToString() : "UnhandledException"
                }
            };

            // OnFailure is fire-and-forget
            _ = SendEventAsync(endpoint, "ON_FAILURE", payload, isBlocking: false);
        }

        public async Task<ControlPointResponse> InvokeBlockingRequestAsync(string endpointUrl, string eventType, object payload)
        {
            return await SendEventAsync(endpointUrl, eventType, payload, isBlocking: true);
        }

        private async Task<ControlPointResponse> SendEventAsync(string endpointUrl, string eventType, object payload, bool isBlocking)
        {
            var executionId = Guid.NewGuid();

            var envelope = new
            {
                toolName = _toolName,
                toolVersion = _toolVersion,
                executionId,
                eventType,
                payload
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var jsonContent = JsonSerializer.Serialize(envelope, jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                var timeoutSeconds = GetInt("CONTROL_POINT_TIMEOUT_SECONDS", 30);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

                using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl) { Content = content };
                request.Headers.Add("X-3SC-Tool", _toolName);
                request.Headers.Add("X-3SC-Execution-ID", executionId.ToString());

                _logger.LogInformation("Invoking Control Point. Event: {EventType}, Endpoint: {Endpoint}", eventType, endpointUrl);
                var response = await _httpClient.SendAsync(request, cts.Token);
                var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Control Point '{EventType}' completed successfully with status {StatusCode}.", eventType, response.StatusCode);
                    return new ControlPointResponse(true, responseBody);
                }
                else
                {
                    _logger.LogError("Control Point '{EventType}' failed with status {StatusCode}: {ResponseBody}", eventType, response.StatusCode, responseBody);
                    return new ControlPointResponse(false, $"Endpoint returned status {response.StatusCode}: {responseBody}");
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Control Point invocation to '{Endpoint}' timed out.", endpointUrl);
                var timeoutAction = GetString("CONTROL_POINT_TIMEOUT_ACTION", "fail");
                if (isBlocking && timeoutAction.Equals("fail", StringComparison.OrdinalIgnoreCase))
                {
                    return new ControlPointResponse(false, $"Timeout after {GetString("CONTROL_POINT_TIMEOUT_SECONDS", "30")} seconds.");
                }
                return new ControlPointResponse(true, "Timeout occurred, but action is set to 'continue'."); // Treat as success to not block
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Control Point invocation to '{Endpoint}' failed with an unexpected exception.", endpointUrl);
                if (isBlocking)
                {
                    return new ControlPointResponse(false, $"An unexpected exception occurred: {ex.Message}");
                }
                // For non-blocking, we don't care about the response
                return new ControlPointResponse(false, string.Empty);
            }
        }

        private string GetString(string suffix, string defaultValue = "")
        {
            return Environment.GetEnvironmentVariable($"ASSEMBLER_{suffix}") ?? Environment.GetEnvironmentVariable($"3SC_{suffix}") ?? defaultValue;
        }

        private int GetInt(string suffix, int defaultValue)
        {
            var valueStr = GetString(suffix);
            return int.TryParse(valueStr, out var result) ? result : defaultValue;
        }
    }
}