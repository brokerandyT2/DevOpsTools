using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    /// <summary>
    /// Defines the contract for the service that manages Control Point webhooks.
    /// </summary>
    public interface IControlPointService
    {
        Task NotifyAsync<T>(ControlPointStage stage, ControlPointEvent eventType, T data);
        Task<T> InterceptAsync<T>(ControlPointStage stage, T data);
    }

    /// <summary>
    /// Implements the logic for discovering and invoking Control Point webhooks based on environment variables.
    /// </summary>
    public class ControlPointService : IControlPointService
    {
        private readonly ILogger<ControlPointService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonOptions;

        private static readonly string _toolName = Assembly.GetExecutingAssembly().GetName().Name ?? "sql-schema-generator";
        private static readonly string _toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public ControlPointService(ILogger<ControlPointService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
        }

        public async Task NotifyAsync<T>(ControlPointStage stage, ControlPointEvent eventType, T data)
        {
            var webhookUrl = GetWebhookUrl(stage, eventType);
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;

            _logger.LogInformation("🚀 Invoking non-blocking Control Point: {Stage}_{EventType}", stage, eventType);

            _ = Task.Run(async () =>
            {
                try
                {
                    await SendPayloadAsync(webhookUrl, stage, data);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Non-blocking Control Point {Stage}_{EventType} failed.", stage, eventType);
                }
            });

            await Task.CompletedTask;
        }

        public async Task<T> InterceptAsync<T>(ControlPointStage stage, T data)
        {
            var webhookUrl = GetWebhookUrl(stage);
            if (string.IsNullOrWhiteSpace(webhookUrl)) return data;

            _logger.LogInformation("🚀 Invoking blocking Control Point: {Stage}. Awaiting response...", stage);

            try
            {
                using var response = await SendPayloadAsync(webhookUrl, stage, data);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var controlPointResponse = JsonSerializer.Deserialize<ControlPointResponse<T>>(responseJson, _jsonOptions);

                if (!string.IsNullOrWhiteSpace(controlPointResponse?.Message))
                {
                    _logger.LogInformation("💬 Message from Control Point {Stage}: {Message}", stage, controlPointResponse.Message);
                }

                if (controlPointResponse?.Action == ControlPointAction.Abort)
                {
                    throw new ControlPointAbortedException(controlPointResponse.Message ?? $"Execution aborted by Control Point '{stage}'.");
                }

                return controlPointResponse.Data ?? data;
            }
            catch (ControlPointAbortedException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blocking Control Point {Stage} failed or returned an invalid response. Aborting for safety.", stage);
                throw new SqlSchemaException(SqlSchemaExitCode.DeploymentExecutionFailure, $"Blocking Control Point '{stage}' failed: {ex.Message}", ex);
            }
        }

        private async Task<HttpResponseMessage> SendPayloadAsync<T>(string url, ControlPointStage stage, T data)
        {
            var payload = new ControlPointPayload<T>
            {
                ToolName = _toolName,
                ToolVersion = _toolVersion,
                Stage = stage.ToString(),
                Data = data
            };

            var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("ControlPointClient");
            client.Timeout = TimeSpan.FromMinutes(5); // Generous timeout for hooks that might do complex validation.

            return await client.PostAsync(url, content);
        }

        private string GetWebhookUrl(ControlPointStage stage, ControlPointEvent? eventType = null)
        {
            var stageName = stage.ToString().ToUpperInvariant();
            var eventName = eventType?.ToString().ToUpperInvariant();

            // Format: SQLSYNC_CP_{STAGE} for blocking, SQLSYNC_CP_{STAGE}_{EVENT} for non-blocking
            var variableName = eventType.HasValue
                ? $"SQLSYNC_CP_{stageName}_{eventName}"
                : $"SQLSYNC_CP_{stageName}";

            return Environment.GetEnvironmentVariable(variableName);
        }
    }
}