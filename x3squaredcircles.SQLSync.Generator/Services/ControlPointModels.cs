using System;
using System.Text.Json.Serialization;
using x3squaredcircles.SQLSync.Generator.Models;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    /// <summary>
    /// Defines the stages in the generator's lifecycle where a Control Point can be invoked.
    /// Matches the enum in SqlSchemaModels for consistency.
    /// </summary>
    public enum ControlPointStage
    {
        OnRunStart,
        AfterDiscovery,
        AfterValidation,
        AfterRiskAssessment,
        BeforeBackup,
        BeforeExecution,
        Completion
    }

    /// <summary>
    /// Defines the specific events within a stage that can trigger a Control Point.
    /// </summary>
    public enum ControlPointEvent
    {
        OnSuccess,
        OnFailure
    }

    /// <summary>
    /// Defines the actions a blocking Control Point can instruct the tool to take.
    /// Matches the enum in SqlSchemaModels for consistency.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ControlPointAction
    {
        Continue,
        Abort
    }

    /// <summary>
    /// Represents the generic payload sent to a Control Point webhook.
    /// Matches the model in SqlSchemaModels for consistency.
    /// </summary>
    /// <typeparam name="T">The type of the data being sent.</typeparam>
    public class ControlPointPayload<T>
    {
        public string ToolName { get; set; }
        public string ToolVersion { get; set; }
        public string Stage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public T Data { get; set; }
    }

    /// <summary>
    /// Represents the expected response from a blocking Control Point webhook.
    /// Matches the model in SqlSchemaModels for consistency.
    /// </summary>
    /// <typeparam name="T">The type of the data being returned, which may have been modified by the webhook.</typeparam>
    public class ControlPointResponse<T>
    {
        public ControlPointAction Action { get; set; } = ControlPointAction.Continue;
        public string Message { get; set; }
        public T Data { get; set; }
    }
}