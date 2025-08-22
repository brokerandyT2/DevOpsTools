using System.Collections.Generic;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Models.WorkItems;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for a specific work item provider client.
    /// Each implementation is responsible for communicating with a single, specific
    /// work item management system (e.g., Jira, Azure DevOps).
    /// </summary>
    public interface IWorkItemProvider
    {
        /// <summary>
        /// Gets the type of provider this implementation represents.
        /// This is used by the provider manager to select the correct client.
        /// </summary>
        WorkItemProviderType ProviderType { get; }

        /// <summary>
        /// Enriches a collection of `WorkItem` objects with details from the provider's API.
        /// </summary>
        /// <remarks>
        /// This method should be implemented to be resilient. If a specific work item ID
        /// is not found in the provider system (e.g., due to a typo in a commit message),
        /// it should be skipped without throwing an exception. The method should enrich all
        /// the work items it can find and leave the others in their original, unenriched state.
        /// </remarks>
        /// <param name="providerUrl">The base URL of the work item provider instance (e.g., "https://mycompany.atlassian.net").</param>
        /// <param name="personalAccessToken">The Personal Access Token (PAT) for authenticating with the API.</param>
        /// <param name="workItemsToEnrich">An enumerable of `WorkItem` objects, each containing a raw ID to be looked up.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result is the same collection of
        /// `WorkItem` objects, with the details (Title, Url, etc.) populated for all items that were successfully found.
        /// </returns>
        Task<IEnumerable<WorkItem>> EnrichWorkItemsAsync(
            string providerUrl,
            string personalAccessToken,
            IEnumerable<WorkItem> workItemsToEnrich);
    }
}