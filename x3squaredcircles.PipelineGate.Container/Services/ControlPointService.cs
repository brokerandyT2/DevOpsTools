using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.PipelineGate.Container.Models;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    public class ControlPointService : IControlPointService
    {
        private readonly ILogger<ControlPointService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonOptions;

        private static readonly string _toolName = Assembly.GetExecutingAssembly().GetName().Name ?? "pipeline-gate-controller";
        private static readonly string _toolVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public ControlPointService(ILogger<ControlPointService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }

        public async Task<GateAction> InterceptDecisionAsync(ControlPointData data)
        {
            var webhookUrl = GetWebhookUrl();
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                return data.ProposedAction; // No Control Point configured, proceed with the tool's decision.
            }

            _logger.LogInformation("🚀 Invoking BeforeDecision Control Point. Awaiting response...");

            try
            {
                var payload = new ControlPointPayload
                {
                    ToolName = _toolName,
                    ToolVersion = _toolVersion,
                    Stage = ControlPointStage.BeforeDecision,
                    Data = data
                };

                var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var client = _httpClientFactory.CreateClient("ControlPointClient");
                client.Timeout = TimeSpan.FromMinutes(5);

                using var response = await client.PostAsync(webhookUrl, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var controlPointResponse = JsonSerializer.Deserialize<ControlPointResponse>(responseJson, _jsonOptions);

                if (controlPointResponse == null)
                {
                    _logger.LogWarning("Control Point returned an empty or invalid response. Continuing with original action.");
                    return data.ProposedAction;
                }

                if (!string.IsNullOrWhiteSpace(controlPointResponse.Message))
                {
                    _logger.LogInformation("💬 Message from Control Point: {Message}", controlPointResponse.Message);
                }

                if (controlPointResponse.Action == ControlPointAction.Override && controlPointResponse.OverrideAction.HasValue)
                {
                    _logger.LogWarning("OVERRIDE: Control Point has overridden the proposed action '{Proposed}' with '{Override}'.", data.ProposedAction, controlPointResponse.OverrideAction.Value);
                    return controlPointResponse.OverrideAction.Value;
                }

                return data.ProposedAction;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BeforeDecision Control Point failed. Aborting for safety.");
                throw new ControlPointException($"Control Point webhook failed: {ex.Message}");
            }
        }

        private string GetWebhookUrl()
        {
            // Naming convention: GATE_CP_{STAGE}
            return Environment.GetEnvironmentVariable("GATE_CP_BEFORE_DECISION");
        }
    }
}