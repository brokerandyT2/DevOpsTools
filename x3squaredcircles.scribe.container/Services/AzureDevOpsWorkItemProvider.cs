using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Models.WorkItems;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements IWorkItemProvider for interacting with the Azure DevOps REST API.
    /// This implementation supports both Azure DevOps Services (cloud) and Azure DevOps Server (on-premise).
    /// </summary>
    public class AzureDevOpsWorkItemProvider : IWorkItemProvider
    {
        private readonly ILogger<AzureDevOpsWorkItemProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        // A host-agnostic regex to parse the first two segments (organization/collection and project) from a URL's path.
        private static readonly Regex AdoPathRegex = new Regex(@"^/([^/]+)/([^/]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <inheritdoc />
        public WorkItemProviderType ProviderType => WorkItemProviderType.AzureDevOps;

        public AzureDevOpsWorkItemProvider(IHttpClientFactory httpClientFactory, ILogger<AzureDevOpsWorkItemProvider> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IEnumerable<WorkItem>> EnrichWorkItemsAsync(
            string providerUrl,
            string personalAccessToken,
            IEnumerable<WorkItem> workItemsToEnrich)
        {
            if (workItemsToEnrich == null || !workItemsToEnrich.Any())
            {
                return Enumerable.Empty<WorkItem>();
            }

            if (!Uri.TryCreate(providerUrl, UriKind.Absolute, out var providerUri))
            {
                _logger.LogError("The provided ADO URL is not a valid absolute URI: {Url}", providerUrl);
                return workItemsToEnrich;
            }

            var (organization, project) = ParseAdoPathFromUrl(providerUri);
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(project))
            {
                _logger.LogError("Could not parse a valid 'organization/project' or 'collection/project' path from the provided ADO URL: {Url}. Aborting enrichment.", providerUrl);
                return workItemsToEnrich; // Return unenriched
            }

            var client = _httpClientFactory.CreateClient();
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{personalAccessToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var workItemMap = workItemsToEnrich.ToDictionary(wi => wi.Id, StringComparer.OrdinalIgnoreCase);
            var numericIds = workItemsToEnrich.Select(ParseNumericId).Where(id => !string.IsNullOrEmpty(id)).ToList();

            if (!numericIds.Any())
            {
                _logger.LogWarning("No valid numeric work item IDs could be parsed. Skipping ADO enrichment.");
                return workItemsToEnrich;
            }

            _logger.LogInformation("Enriching {Count} ADO work items from {Org}/{Proj} on host {Host}.", numericIds.Count, organization, project, providerUri.Host);

            // Dynamically build the API URL from the provided provider URL to support any host.
            var apiBaseUrl = $"{providerUri.Scheme}://{providerUri.Authority}/{organization}/{project}/_apis/wit/workitems";
            var requestUrl = $"{apiBaseUrl}?ids={string.Join(",", numericIds)}&$expand=all&api-version=6.0";

            try
            {
                var response = await client.GetAsync(requestUrl);

                if (response.IsSuccessStatusCode)
                {
                    var jsonStream = await response.Content.ReadAsStreamAsync();
                    var batchResponse = await JsonSerializer.DeserializeAsync<AzureDevOpsWorkItemBatchResponseDto>(jsonStream);

                    if (batchResponse?.Value != null)
                    {
                        foreach (var adoItem in batchResponse.Value)
                        {
                            var matchingItems = workItemMap.Values.Where(wi => ParseNumericId(wi.Id) == adoItem.Id.ToString());
                            foreach (var itemToUpdate in matchingItems)
                            {
                                itemToUpdate.Title = adoItem.Fields.Title;
                                itemToUpdate.Type = adoItem.Fields.WorkItemType;
                                itemToUpdate.Url = new Uri(adoItem.Links.Html.Href);
                                itemToUpdate.IsEnriched = true;
                                _logger.LogDebug("Successfully enriched ADO work item #{Id}: {Title}", adoItem.Id, adoItem.Fields.Title);
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to fetch ADO work items. Status: {StatusCode}. Reason: {Reason}", response.StatusCode, response.ReasonPhrase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred while trying to enrich ADO work items.");
            }

            return workItemsToEnrich;
        }

        private (string? Organization, string? Project) ParseAdoPathFromUrl(Uri url)
        {
            var match = AdoPathRegex.Match(url.AbsolutePath);
            if (match.Success && match.Groups.Count > 2)
            {
                return (match.Groups[1].Value, match.Groups[2].Value.TrimEnd('/'));
            }
            return (null, null);
        }

        private string? ParseNumericId(WorkItem workItem) => ParseNumericId(workItem.Id);
        private string? ParseNumericId(string workItemId)
        {
            return Regex.Match(workItemId, @"\d+").Value;
        }

        #region DTOs
        private class AzureDevOpsWorkItemBatchResponseDto { [JsonPropertyName("value")] public List<AzureDevOpsWorkItemDto> Value { get; set; } = new(); }
        private class AzureDevOpsWorkItemDto { [JsonPropertyName("id")] public int Id { get; set; } [JsonPropertyName("fields")] public AzureDevOpsWorkItemFieldsDto Fields { get; set; } = new(); [JsonPropertyName("_links")] public AzureDevOpsWorkItemLinksDto Links { get; set; } = new(); }
        private class AzureDevOpsWorkItemFieldsDto { [JsonPropertyName("System.Title")] public string Title { get; set; } = string.Empty; [JsonPropertyName("System.WorkItemType")] public string WorkItemType { get; set; } = string.Empty; }
        private class AzureDevOpsWorkItemLinksDto { [JsonPropertyName("html")] public AzureDevOpsLinkDto Html { get; set; } = new(); }
        private class AzureDevOpsLinkDto { [JsonPropertyName("href")] public string Href { get; set; } = string.Empty; }
        #endregion
    }
}