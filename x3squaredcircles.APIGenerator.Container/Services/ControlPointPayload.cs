using System.Threading.Tasks;

namespace x3squaredcircles.datalink.container.Services
{
    /// <summary>
    /// Defines the different lifecycle events at which a Control Point can be invoked.
    /// </summary>
    public enum ControlPointType
    {
        OnStartup,
        OnSuccess,
        OnFailure,
        // NEW: A dedicated type for the deployment override.
        DeploymentOverride
    }

    /// <summary>
    /// Defines the contract for a service that invokes external webhooks (Control Points).
    /// </summary>
    public interface IControlPointService
    {
        /// <summary>
        /// Invokes a notification-style Control Point webhook (OnStartup, OnSuccess, OnFailure).
        /// </summary>
        Task InvokeNotificationAsync(ControlPointType type, string command, string? errorMessage = null);

        /// <summary>
        /// Invokes the deployment override Control Point, passing all necessary context and
        /// replacing the tool's built-in deployment logic.
        /// </summary>
        /// <returns>True if the external deployment succeeded; otherwise, false.</returns>
        Task<bool> InvokeDeploymentOverrideAsync(DeploymentOverridePayload payload);
    }

    /// <summary>
    /// Represents the simple JSON payload sent to a notification Control Point webhook.
    /// </summary>
    public class ControlPointPayload
    {
        public string EventType { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public string ToolVersion { get; set; } = string.Empty;
        public string TimestampUtc { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents the rich JSON payload sent to the Deployment Override Control Point.
    /// It contains all the context a remote system needs to perform the deployment.
    /// </summary>
    public class DeploymentOverridePayload
    {
        public string ServiceName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string ArtifactPath { get; set; } = string.Empty;
        public string TargetEnvironment { get; set; } = string.Empty;
        public string TargetCloud { get; set; } = string.Empty;
        public string TargetRegion { get; set; } = string.Empty; // Example of additional context
        public string DeploymentPattern { get; set; } = string.Empty;
    }
}