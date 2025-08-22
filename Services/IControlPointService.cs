using System;
using System.Threading.Tasks;

namespace x3squaredcircles.API.Assembler.Services
{
    /// <summary>
    /// Represents the standardized response from a blocking Control Point invocation.
    /// </summary>
    /// <param name="IsSuccess">Indicates if the call was successful (e.g., received a 2xx status).</param>
    /// <param name="ResponseMessage">The body of the response, used as a success message or an error reason.</param>
    public record ControlPointResponse(bool IsSuccess, string ResponseMessage);

    public interface IControlPointService
    {
        /// <summary>
        /// Invokes the non-blocking OnStartup lifecycle event.
        /// </summary>
        Task InvokeOnStartupAsync();

        /// <summary>
        /// Invokes the non-blocking OnSuccess lifecycle event.
        /// </summary>
        Task InvokeOnSuccessAsync();

        /// <summary>
        /// Invokes the non-blocking OnFailure lifecycle event.
        /// </summary>
        Task InvokeOnFailureAsync(Exception ex);

        /// <summary>
        /// Invokes a generic, synchronous, blocking Control Point and waits for a definitive success/fail response.
        /// </summary>
        /// <param name="endpointUrl">The URL of the Control Point endpoint.</param>
        /// <param name="eventType">The specific event type for this call (e.g., "DEPLOYMENT_TOOL").</param>
        /// <param name="payload">The data object to be serialized and sent as the request body.</param>
        /// <returns>A ControlPointResponse indicating the outcome of the call.</returns>
        Task<ControlPointResponse> InvokeBlockingRequestAsync(string endpointUrl, string eventType, object payload);
    }
}