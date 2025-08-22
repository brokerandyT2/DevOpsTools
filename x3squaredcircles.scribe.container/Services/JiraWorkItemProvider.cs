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
    /// Implements IWorkItemProvider for interacting with the Jira Cloud REST API.
    /// </summary>
    public class JiraWorkItemProvider : IWorkItemProvider
    {
        private readonly ILogger<JiraWorkItemProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        // A specific regex to validate that an ID looks like a Jira key before sending it to the API.
        private static readonly Regex JiraKeyRegex = new Regex(@"\b([A-Z][A-Z0-9]+-\d+)\b", RegexOptions.Compiled);

        /// <inheritdoc />
        public WorkItemProviderType ProviderType => WorkItemProviderType.Jira;

        public JiraWorkItemProvider(IHttpClientFactory httpClientFactory, ILogger<JiraWorkItemProvider> logger)
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

            var jiraKeys = workItemsToEnrich
                .Select(wi => JiraKeyRegex.Match(wi.Id).Value)
                .Where(key => !string.IsNullOrEmpty(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!jiraKeys.Any())
            {
                _logger.LogWarning("No valid Jira-formatted work item keys were found to enrich.");
                return workItemsToEnrich;
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var workItemMap = workItemsToEnrich.ToDictionary(wi => wi.Id, StringComparer.OrdinalIgnoreCase);
            var apiEndpoint = new Uri(new Uri(providerUrl), "/rest/api/2/search");

            _logger.LogInformation("Enriching {Count} Jira issues from host: {Host}", jiraKeys.Count, apiEndpoint.Host);

            var jql = $"issuekey in ({string.Join(",", jiraKeys)})";
            var requestBody = new JiraSearchRequestDto(jql);
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync(apiEndpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonStream = await response.Content.ReadAsStreamAsync();
                    var searchResult = await JsonSerializer.DeserializeAsync<JiraSearchResponseDto>(jsonStream);

                    if (searchResult?.Issues != null)
                    {
                        foreach (var jiraIssue in searchResult.Issues)
                        {
                            if (workItemMap.TryGetValue(jiraIssue.Key, out var itemToUpdate))
                            {
                                itemToUpdate.Title = jiraIssue.Fields.Summary;
                                itemToUpdate.Type = jiraIssue.Fields.IssueType.Name;
                                itemToUpdate.Url = new Uri(new Uri(providerUrl), $"browse/{jiraIssue.Key}");
                                itemToUpdate.IsEnriched = true;
                                _logger.LogDebug("Successfully enriched Jira issue {Key}: {Summary}", jiraIssue.Key, jiraIssue.Fields.Summary);
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to fetch Jira issues. Status: {StatusCode}. Reason: {Reason}", response.StatusCode, response.ReasonPhrase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred while trying to enrich Jira issues.");
            }

            return workItemsToEnrich;
        }

        #region DTOs
        // Private DTOs to build the request and deserialize the response from the Jira API.
        private class JiraSearchRequestDto
        {
            [JsonPropertyName("jql")]
            public string Jql { get; }
            [JsonPropertyName("fields")]
            public string[] Fields { get; } = { "summary", "issuetype" };

            public JiraSearchRequestDto(string jql) => Jql = jql;
        }

        private class JiraSearchResponseDto
        {
            [JsonPropertyName("issues")]
            public List<JiraIssueDto> Issues { get; set; } = new List<JiraIssueDto>();
        }

        private class JiraIssueDto
        {
            [JsonPropertyName("key")]
            public string Key { get; set; } = string.Empty;
            [JsonPropertyName("fields")]
            public JiraIssueFieldsDto Fields { get; set; } = new JiraIssueFieldsDto();
        }

        private class JiraIssueFieldsDto
        {
            [JsonPropertyName("summary")]
            public string Summary { get; set; } = string.Empty;
            [JsonPropertyName("issuetype")]
            public JiraIssueTypeDto IssueType { get; set; } = new JiraIssueTypeDto();
        }

        private class JiraIssueTypeDto
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }
        #endregion
    }
}