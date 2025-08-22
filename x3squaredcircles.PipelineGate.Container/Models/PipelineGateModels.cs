using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace x3squaredcircles.PipelineGate.Container.Models
{
    #region Core Enums and Exceptions

    /// <summary>
    /// Defines the operational modes for the Pipeline Gate Controller.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GateMode
    {
        Basic,
        Advanced,
        Custom
    }

    /// <summary>
    /// Defines the sub-commands for the Advanced asynchronous workflow mode.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AdvancedModeAction
    {
        Notify,
        WaitFor
    }

    /// <summary>
    /// Defines the final decision actions the gate can take.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GateAction
    {
        Pass,
        Pause,
        Break
    }

    /// <summary>
    /// Defines the exit codes for the application.
    /// </summary>
    public enum GateExitCode
    {
        Pass = 0,
        UnhandledException = 1,
        InvalidConfiguration = 2,
        ApiConnectionFailure = 3,
        EvaluationFailure = 4,
        Timeout = 5,
        ControlPointAborted = 6,
        Pause = 70,
        Break = 71
    }

    public class PipelineGateException : Exception
    {
        public GateExitCode ExitCode { get; }
        public PipelineGateException(GateExitCode exitCode, string message) : base(message) { ExitCode = exitCode; }
        public PipelineGateException(GateExitCode exitCode, string message, Exception innerException) : base(message, innerException) { ExitCode = exitCode; }
    }

    #endregion

    #region Configuration Models

    public class GateConfiguration
    {
        // Universal Config
        public VaultConfiguration Vault { get; set; } = new();
        public LoggingConfiguration Logging { get; set; } = new();
        public ObservabilityConfiguration Observability { get; set; } = new();

        // Core Operational Config
        public GateMode Mode { get; set; }
        public string CiRunId { get; set; }

        // Mode-Specific Config Blocks
        public BasicModeConfiguration Basic { get; set; } = new();
        public AdvancedModeConfiguration Advanced { get; set; } = new();
        public CustomModeConfiguration Custom { get; set; } = new();
    }

    public class VaultConfiguration
    {
        public VaultType Type { get; set; }
        public string Url { get; set; }
    }
    public enum VaultType { None, Azure, Aws, HashiCorp }

    public class LoggingConfiguration
    {
        public LogLevel LogLevel { get; set; }
    }

    public class ObservabilityConfiguration
    {
        public string FirehoseLogEndpointUrl { get; set; }
        public string FirehoseLogEndpointToken { get; set; }
    }

    // --- Mode-Specific Configuration ---
    public class BasicModeConfiguration
    {
        public string Url { get; set; }
        public string SecretName { get; set; }
        public string SuccessEval { get; set; }
        public GateAction DefaultAction { get; set; } = GateAction.Break;
    }

    public class AdvancedModeConfiguration
    {
        public AdvancedModeAction Action { get; set; }
        // Notify Config
        public string NotifyUrl { get; set; }
        public string NotifyPayload { get; set; }
        // WaitFor Config
        public string WaitUrl { get; set; }
        public string WaitSecretName { get; set; }
        public string WaitSuccessEval { get; set; }
        public string WaitFailureEval { get; set; }
        public GateAction WaitDefaultAction { get; set; } = GateAction.Pause;
        public int WaitTimeoutMinutes { get; set; } = 30;
        public int WaitPollIntervalSeconds { get; set; } = 15;
    }

    public class CustomModeConfiguration
    {
        public string SwaggerPath { get; set; }
        public string SecretName { get; set; }
        public string OperationId { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
        public string SuccessEval { get; set; }
        public string FailureEval { get; set; }
        public GateAction DefaultAction { get; set; } = GateAction.Break;
    }

    #endregion

    #region Control Point Models
    public enum ControlPointStage { BeforeDecision }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ControlPointAction { Continue, Override }

    public class ControlPointPayload
    {
        public string ToolName { get; set; }
        public string ToolVersion { get; set; }
        public ControlPointStage Stage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public ControlPointData Data { get; set; }
    }

    public class ControlPointData
    {
        public GateMode Mode { get; set; }
        public string EndpointUrl { get; set; }
        public int HttpResponseStatusCode { get; set; }
        public string HttpResponseBody { get; set; }
        public EvaluationResult InitialEvaluation { get; set; }
        public GateAction ProposedAction { get; set; }
        public Dictionary<string, string> CustomParameters { get; set; } = new();
    }

    public class EvaluationResult
    {
        public string Condition { get; set; }
        public bool Passed { get; set; }
    }

    public class ControlPointResponse
    {
        public ControlPointAction Action { get; set; } = ControlPointAction.Continue;
        public GateAction? OverrideAction { get; set; }
        public string Message { get; set; }
    }

    public class ControlPointException : PipelineGateException
    {
        public ControlPointException(string message)
            : base(GateExitCode.ControlPointAborted, message) { }
    }
    #endregion
}