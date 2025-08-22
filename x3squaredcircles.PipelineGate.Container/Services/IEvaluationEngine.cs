using System.Threading.Tasks;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    /// <summary>
    /// Defines the contract for the engine that parses and evaluates conditions
    /// against JSON or XML data.
    /// </summary>
    public interface IEvaluationEngine
    {
        /// <summary>
        /// Evaluates a condition string against a given response content.
        /// </summary>
        /// <param name="condition">The condition to evaluate (e.g., "jsonpath($.status) == 'Approved'").</param>
        /// <param name="responseContent">The raw JSON or XML string from an HTTP response.</param>
        /// <param name="contentType">The content type of the response (e.g., "application/json").</param>
        /// <returns>True if the condition is met, otherwise false.</returns>
        Task<bool> EvaluateConditionAsync(string condition, string responseContent, string contentType);
    }
}