using System.Net.Http;
using System.Threading.Tasks;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    /// <summary>
    /// Defines the contract for a service that handles all outbound HTTP communications.
    /// </summary>
    public interface IHttpService
    {
        /// <summary>
        /// Executes a fire-and-forget POST request for notification purposes.
        /// </summary>
        /// <param name="url">The target URL.</param>
        /// <param name="payload">The JSON payload to send.</param>
        /// <returns>A task that completes when the request has been sent.</returns>
        Task NotifyAsync(string url, string payload);

        /// <summary>
        /// Executes an HTTP request and returns the response for evaluation.
        /// </summary>
        /// <param name="url">The target URL.</param>
        /// <param name="secretName">The optional name of the secret in the vault containing the auth token.</param>
        /// <returns>An HttpResponseMessage containing the result of the call.</returns>
        Task<HttpResponseMessage> SendRequestAsync(string url, string secretName = null);
    }
}