using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    /// <summary>
    /// Defines the major stages of the design token workflow for Control Point invocation.
    /// </summary>
    public enum ControlPointStage
    {
        RunStart,
        Extract,
        Generate,
        Commit,
        RunEnd
    }

    /// <summary>
    /// Defines the contract for a service that invokes external webhooks (Control Points)
    /// at key stages of the application's lifecycle.
    /// </summary>
    public interface IControlPointService
    {
        /// <summary>
        /// Invokes a specific Control Point webhook if it has been configured.
        /// </summary>
        /// <param name="stage">The workflow stage invoking the control point.</param>
        /// <param name="eventName">The specific event name (e.g., "OnSuccess", "BeforeCommit").</param>
        /// <param name="isBlocking">Whether the tool should wait for the webhook to complete.</param>
        /// <param name="payloadMetadata">Optional metadata to include in the payload.</param>
        /// <returns>True if the invocation was successful or not required; false for a blocking failure.</returns>
        Task<bool> InvokeAsync(ControlPointStage stage, string eventName, bool isBlocking = false, Dictionary<string, object>? payloadMetadata = null);
    }
}