using System.Threading.Tasks;
using x3squaredcircles.PipelineGate.Container.Models;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    /// <summary>
    /// Defines the contract for the service that manages Control Point webhooks.
    /// </summary>
    public interface IControlPointService
    {
        /// <summary>
        /// Invokes the blocking BeforeDecision Control Point. This hook can inspect the tool's
        /// proposed action and override it.
        /// </summary>
        /// <param name="data">The context data payload for the Control Point.</param>
        /// <returns>The final GateAction, which may have been overridden by the webhook.</returns>
        Task<GateAction> InterceptDecisionAsync(ControlPointData data);
    }
}