using System;
using System.Text.Json.Serialization;
using x3squaredcircles.MobileAdapter.Generator.Models;

namespace x3squaredcircles.MobileAdapter.Generator.Core
{
    /// <summary>
    /// Defines the stages in the generator's lifecycle where a Control Point can be invoked.
    /// </summary>
    public enum ControlPointStage
    {
        Discovery,
        Generation,
        Completion
    }

    /// <summary>
    /// Defines the specific events within a stage that can trigger a Control Point.
    /// </summary>
    public enum ControlPointEvent
    {
        Before,
        After,
        OnSuccess,
        OnFailure
    }

    /// <summary>
    /// Defines the actions a blocking Control Point can instruct the tool to take.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ControlPointAction
    {
        /// <summary>
        /// Instructs the tool to continue its execution.
        /// </summary>
        Continue,
        /// <summary>
        /// Instructs the tool to halt execution gracefully.
        /// </summary>
        Abort
    }

    /// <summary>
    /// Represents the generic payload sent to a Control Point webhook.
    /// </summary>
    /// <typeparam name="T">The type of the data being sent.</typeparam>
    public class ControlPointPayload<T>
    {
        public string ToolName { get; set; }
        public string ToolVersion { get; set; }
        public string Stage { get; set; }
        public string Event { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public T Data { get; set; }
    }

    /// <summary>
    /// Represents the expected response from a blocking Control Point webhook.
    /// </summary>
    /// <typeparam name="T">The type of the data being returned, which may have been modified by the webhook.</typeparam>
    public class ControlPointResponse<T>
    {
        /// <summary>
        /// The action the tool should take after the hook completes. Defaults to Continue.
        /// </summary>
        public ControlPointAction Action { get; set; } = ControlPointAction.Continue;

        /// <summary>
        /// An optional message from the webhook to be logged by the tool.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The payload data, potentially modified by the webhook. If null, the tool will continue with its original data.
        /// </summary>
        public T Data { get; set; }
    }

    /// <summary>
    /// Custom exception thrown when a Control Point webhook instructs the tool to abort its operation.
    /// </summary>
    public class ControlPointAbortedException : MobileAdapterException
    {
        public ControlPointAbortedException(string message)
            : base(MobileAdapterExitCode.Success, message) // Exit code is Success because it's a graceful, user-directed stop.
        {
        }
    }
}