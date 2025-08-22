using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Models.WorkItems;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements IWorkItemProvider for interacting with the GitHub Issues API.
    /// This implementation supports both GitHub.com (cloud) and GitHub Enterprise (on-premise).
    /// </summary>
    public class GitHubWorkItemProvider : IWorkItemProvider
    {
        private readonly ILogger<GitHubWorkItemProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        // Regex to parse "owner/repo" from the path of a GitHub URL, ignoring the hostname.
        private static readonly Regex RepoPathRegex = new Regex(@"^/([^/]+/[^/]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <inheritdoc />
        public WorkItemProviderType ProviderType => WorkItemProviderType.GitHub;

        public GitHubWorkItemProvider(IHttpClientFactory httpClientFactory, ILogger<GitHubWorkItemProvider> logger)
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
                _logger.LogError("The provided GitHub URL is not a valid absolute URI: {Url}", providerUrl);
                return workItemsToEnrich;
            }

            var repoPath = ParseRepoPathFromUrl(providerUri);
            if (string.IsNullOrEmpty(repoPath))
            {
                _logger.LogError("Could not parse a valid 'owner/repository' path from the provided GitHub URL: {Url}. Aborting enrichment.", providerUrl);
                return workItemsToEnrich;
            }

            var apiBaseUrl = GetApiBaseUrl(providerUri);
            var issuesEndpoint = new Uri(apiBaseUrl, $"repos/{repoPath}/issues/");

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("3SC-Scribe", "5.0"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            _logger.LogInformation("Enriching GitHub issues from repository: {RepoPath} on host {Host}", repoPath, providerUri.Host);

            foreach (var workItem in workItemsToEnrich)
            {
                var issueNumber = ParseIssueNumber(workItem.Id);
                if (string.IsNullOrEmpty(issueNumber))
                {
                    _logger.LogWarning("Could not parse a valid issue number from work item ID '{WorkItemId}'. Skipping.", workItem.Id);
                    continue;
                }

                var requestUrl = new Uri(issuesEndpoint, issueNumber);

                try
                {
                    var response = await client.GetAsync(requestUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonStream = await response.Content.ReadAsStreamAsync();
                        var issue = await JsonSerializer.DeserializeAsync<GitHubIssueDto>(jsonStream);
                        if (issue != null)
                        {
                            workItem.Title = issue.Title;
                            workItem.Url = new Uri(issue.HtmlUrl);
                            workItem.Type = issue.Labels.FirstOrDefault()?.Name ?? "Issue";
                            workItem.IsEnriched = true;
                            _logger.LogDebug("Successfully enriched GitHub issue #{IssueNumber}: {Title}", issueNumber, issue.Title);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to fetch GitHub issue #{IssueNumber}. Status: {StatusCode}. Reason: {Reason}", issueNumber, response.StatusCode, response.ReasonPhrase);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred while trying to enrich GitHub issue #{IssueNumber}.", issueNumber);
                }
            }

            return workItemsToEnrich;
        }

        private Uri GetApiBaseUrl(Uri providerUri)
        {
            // For GitHub.com, the API is at api.github.com.
            if (providerUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri("https://api.github.com/");
            }
            // For GitHub Enterprise, the API is at the same host under /api/v3.
            return new Uri($"{providerUri.Scheme}://{providerUri.Authority}/api/v3/");
        }

        private string? ParseRepoPathFromUrl(Uri url)
        {
            var match = RepoPathRegex.Match(url.AbsolutePath);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.TrimEnd('/');
            }
            return null;
        }

        private string? ParseIssueNumber(string workItemId)
        {
            return Regex.Match(workItemId, @"\d+").Value;
        }

        #region DTOs
        private class GitHubIssueDto { [JsonPropertyName("title")] public string Title { get; set; } = string.Empty; [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = string.Empty; [JsonPropertyName("labels")] public List<GitHubLabelDto> Labels { get; set; } = new(); }
        private class GitHubLabelDto { [JsonPropertyName("name")] public string Name { get; set; } = string.Empty; }
        #endregion
    }
}