using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;

namespace x3squaredcircles.MobileAdapter.Generator.Core
{
    /// <summary>
    /// Defines the contract for the service that manages Control Point webhooks.
    // </summary>
    public interface IControlPointService
    {
        /// <summary>
        /// Invokes a non-blocking, fire-and-forget webhook for notification purposes.
        /// </summary>
        Task NotifyAsync<T>(ControlPointStage stage, ControlPointEvent eventType, T data);

        /// <summary>
        /// Invokes a blocking webhook that can inspect and potentially modify data or halt execution.
        /// </summary>
        /// <returns>The original or modified data payload from the webhook response.</returns>
        Task<T> InterceptAsync<T>(ControlPointStage stage, ControlPointEvent eventType, T data);
    }

    /// <summary>
    /// Implements the logic for discovering and invoking Control Point webhooks based on environment variables.
    /// </summary>
    public class ControlPointService : IControlPointService
    {
        private readonly ILogger<ControlPointService> _logger;
        private readonly GeneratorConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonOptions;

        private static readonly string _toolName = Assembly.GetExecutingAssembly().GetName().Name ?? "mobile-adapter-generator";
        private static readonly string _toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public ControlPointService(ILogger<ControlPointService> logger, GeneratorConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _config = config;
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
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                return; // No webhook configured for this event.
            }

            _logger.LogInformation("🚀 Invoking non-blocking Control Point: {Stage}_{EventType}", stage, eventType);

            // Fire-and-forget this operation. We don't await it, but we handle exceptions to prevent unobserved task exceptions.
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendPayloadAsync(webhookUrl, stage, eventType, data);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Non-blocking Control Point {Stage}_{EventType} failed.", stage, eventType);
                }
            });

            await Task.CompletedTask;
        }

        public async Task<T> InterceptAsync<T>(ControlPointStage stage, ControlPointEvent eventType, T data)
        {
            var webhookUrl = GetWebhookUrl(stage, eventType);
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                return data; // No webhook configured, return original data immediately.
            }

            _logger.LogInformation("🚀 Invoking blocking Control Point: {Stage}_{EventType}. Awaiting response...", stage, eventType);

            try
            {
                using var response = await SendPayloadAsync(webhookUrl, stage, eventType, data);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var controlPointResponse = JsonSerializer.Deserialize<ControlPointResponse<T>>(responseJson, _jsonOptions);

                if (!string.IsNullOrWhiteSpace(controlPointResponse?.Message))
                {
                    _logger.LogInformation("💬 Message from Control Point: {Message}", controlPointResponse.Message);
                }

                if (controlPointResponse?.Action == ControlPointAction.Abort)
                {
                    throw new ControlPointAbortedException(controlPointResponse.Message ?? "Execution aborted by Control Point.");
                }

                // If the webhook returns a modified data payload, use it. Otherwise, use the original data.
                return controlPointResponse.Data ?? data;
            }
            catch (ControlPointAbortedException)
            {
                // Re-throw to be caught by the main engine.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blocking Control Point {Stage}_{EventType} failed or returned an invalid response. Continuing with original data.", stage, eventType);
                return data; // On failure, default to continuing the operation.
            }
        }

        private async Task<HttpResponseMessage> SendPayloadAsync<T>(string url, ControlPointStage stage, ControlPointEvent eventType, T data)
        {
            var payload = new ControlPointPayload<T>
            {
                ToolName = _toolName,
                ToolVersion = _toolVersion,
                Stage = stage.ToString(),
                Event = eventType.ToString(),
                Data = data
            };

            var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("ControlPointClient");
            client.Timeout = TimeSpan.FromSeconds(60); // Generous timeout for blocking hooks.

            return await client.PostAsync(url, content);
        }

        private string GetWebhookUrl(ControlPointStage stage, ControlPointEvent eventType)
        {
            // Naming convention: TOOLNAME_CP_{STAGE}_{EVENT}
            var variableName = $"ADAPTERGEN_CP_{stage.ToString().ToUpper()}_{eventType.ToString().ToUpper()}";
            return Environment.GetEnvironmentVariable(variableName);
        }
    }
}