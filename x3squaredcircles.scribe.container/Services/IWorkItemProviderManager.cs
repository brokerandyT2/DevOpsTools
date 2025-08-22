using System.Collections.Generic;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Models.WorkItems;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Defines the contract for a service that manages the entire work item enrichment process.
    /// This service encapsulates the "Smart Detection Cascade" logic to determine the correct
    /// provider, and then orchestrates the enrichment of work items using that provider.
    /// </summary>
    public interface IWorkItemProviderManager
    {
        /// <summary>
        /// Takes a collection of raw work item IDs, determines the appropriate provider,
        /// and attempts to enrich the work items with details like Title and URL.
        /// </summary>
        /// <remarks>
        /// This method implements the full Smart Detection Cascade:
        /// 1. Checks for explicit provider configuration (SCRIBE_WI_URL, SCRIBE_WI_PAT).
        /// 2. If present, attempts PAT Fingerprinting, then API Handshake.
        /// 3. Falls back to the SCRIBE_WI_PROVIDER variable if smart detection fails.
        /// 4. If no explicit configuration is found, it attempts to use the "ambient" provider.
        /// 5. If at any point an online connection fails or no provider can be determined,
        ///    it gracefully degrades, returning the work items in their original, unenriched state.
        /// </remarks>
        /// <param name="rawWorkItemIds">A collection of unique work item IDs parsed from a source like Git commits.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result is a collection of
        /// <see cref="WorkItem"/> objects. Items that were successfully found and enriched will have
        /// their `IsEnriched` flag set to true, while others will remain in their raw state.
        /// </returns>
        Task<IEnumerable<WorkItem>> EnrichWorkItemsAsync(IEnumerable<string> rawWorkItemIds);
    }
}