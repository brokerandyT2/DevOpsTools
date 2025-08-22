using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Configuration;
using x3squaredcircles.scribe.container.Models.WorkItems;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements the "Smart Detection Cascade" to manage and orchestrate work item providers.
    /// </summary>
    public class WorkItemProviderManager : IWorkItemProviderManager
    {
        private readonly ILogger<WorkItemProviderManager> _logger;
        private readonly ScribeSettings _settings;
        private readonly IEnumerable<IWorkItemProvider> _providers;
        private readonly IHttpClientFactory _httpClientFactory;

        public WorkItemProviderManager(
            IOptions<ScribeSettings> settings,
            IEnumerable<IWorkItemProvider> providers,
            IHttpClientFactory httpClientFactory,
            ILogger<WorkItemProviderManager> logger)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IEnumerable<WorkItem>> EnrichWorkItemsAsync(IEnumerable<string> rawWorkItemIds)
        {
            if (rawWorkItemIds == null || !rawWorkItemIds.Any())
            {
                _logger.LogInformation("No raw work item IDs provided; skipping enrichment.");
                return Enumerable.Empty<WorkItem>();
            }

            var workItems = rawWorkItemIds.Select(id => new WorkItem(id)).ToList();
            var (providerType, url, pat) = await DetectProviderAsync();

            if (providerType == WorkItemProviderType.Unknown)
            {
                _logger.LogWarning("Could not determine a work item provider. Work items will not be enriched (Graceful Degradation).");
                return workItems;
            }

            var provider = _providers.FirstOrDefault(p => p.ProviderType == providerType);
            if (provider == null)
            {
                _logger.LogError("Detected provider type '{ProviderType}' but no implementation is registered in the DI container.", providerType);
                return workItems;
            }

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(pat))
            {
                _logger.LogError("Detected provider '{ProviderType}' but URL and/or PAT are missing.", providerType);
                return workItems;
            }

            _logger.LogInformation("Attempting to enrich {Count} work items using the {ProviderType} provider.", workItems.Count, providerType);
            return await provider.EnrichWorkItemsAsync(url, pat, workItems);
        }

        private async Task<(WorkItemProviderType, string?, string?)> DetectProviderAsync()
        {
            _logger.LogInformation("Starting work item provider detection cascade...");

            // Priority 1: Smart Detection Override
            if (!string.IsNullOrEmpty(_settings.WorkItemUrl) && !string.IsNullOrEmpty(_settings.WorkItemPat))
            {
                _logger.LogDebug("SCRIBE_WI_URL and SCRIBE_WI_PAT are set. Initiating Smart Detection Override.");

                var pat = _settings.WorkItemPat;
                var url = _settings.WorkItemUrl;

                var provider = AttemptPatFingerprinting(pat);
                if (provider != WorkItemProviderType.Unknown)
                {
                    _logger.LogInformation("Provider identified via PAT Fingerprinting: {ProviderType}", provider);
                    return (provider, url, pat);
                }

                provider = await AttemptApiHandshakeAsync(url, pat);
                if (provider != WorkItemProviderType.Unknown)
                {
                    _logger.LogInformation("Provider identified via API Handshake: {ProviderType}", provider);
                    return (provider, url, pat);
                }

                provider = GetProviderFromExplicitSetting();
                if (provider != WorkItemProviderType.Unknown)
                {
                    _logger.LogInformation("Provider identified via explicit SCRIBE_WI_PROVIDER setting: {ProviderType}", provider);
                    return (provider, url, pat);
                }

                _logger.LogWarning("Smart Detection Override failed. No provider could be identified from the given URL and PAT.");
            }

            // Priority 2: Ambient ("Just Works") Detection
            var ambientAdoUrl = Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI");
            var ambientAdoPat = Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");
            if (!string.IsNullOrEmpty(ambientAdoUrl) && !string.IsNullOrEmpty(ambientAdoPat))
            {
                _logger.LogInformation("Detected Azure DevOps via ambient CI variables. Using SYSTEM_COLLECTIONURI.");
                return (WorkItemProviderType.AzureDevOps, ambientAdoUrl, ambientAdoPat);
            }
            // Add other ambient detections here (e.g., GITHUB_SERVER_URL, CI_SERVER_URL)

            _logger.LogInformation("Provider detection cascade complete. No provider could be determined.");
            return (WorkItemProviderType.Unknown, null, null);
        }

        private WorkItemProviderType AttemptPatFingerprinting(string pat)
        {
            _logger.LogDebug("Step A: Attempting PAT Fingerprinting...");
            if (pat.StartsWith("ghp_") || pat.StartsWith("gho_") || pat.StartsWith("ghu_") || pat.StartsWith("ghs_") || pat.StartsWith("ghr_"))
            {
                _logger.LogDebug("PAT prefix matches GitHub pattern.");
                return WorkItemProviderType.GitHub;
            }
            _logger.LogDebug("No known PAT prefix detected.");
            return WorkItemProviderType.Unknown;
        }

        private async Task<WorkItemProviderType> AttemptApiHandshakeAsync(string url, string pat)
        {
            _logger.LogDebug("Step B: Attempting API Handshake...");
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("3SC-Scribe-Detector", "5.0"));

            // Handshake for Jira: Check the /rest/api/2/serverInfo endpoint
            try
            {
                var jiraRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(url), "/rest/api/2/serverInfo"));
                jiraRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
                var jiraResponse = await client.SendAsync(jiraRequest);
                if (jiraResponse.IsSuccessStatusCode)
                {
                    _logger.LogDebug("API Handshake with Jira endpoint succeeded.");
                    return WorkItemProviderType.Jira;
                }
            }
            catch (Exception ex) { _logger.LogTrace(ex, "Jira handshake failed. This is expected if the target is not Jira."); }

            // Handshake for Azure DevOps: Check the /_apis/connectiondata endpoint
            try
            {
                var adoRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(url), "/_apis/connectiondata"));
                var adoCreds = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
                adoRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", adoCreds);
                var adoResponse = await client.SendAsync(adoRequest);
                if (adoResponse.IsSuccessStatusCode)
                {
                    _logger.LogDebug("API Handshake with Azure DevOps endpoint succeeded.");
                    return WorkItemProviderType.AzureDevOps;
                }
            }
            catch (Exception ex) { _logger.LogTrace(ex, "ADO handshake failed. This is expected if the target is not ADO."); }

            _logger.LogDebug("API Handshake failed to identify any provider.");
            return WorkItemProviderType.Unknown;
        }

        private WorkItemProviderType GetProviderFromExplicitSetting()
        {
            var providerString = _settings.WorkItemProvider;
            if (string.IsNullOrWhiteSpace(providerString)) return WorkItemProviderType.Unknown;

            _logger.LogDebug("Evaluating explicit provider setting: '{ProviderString}'", providerString);
            if (Enum.TryParse<WorkItemProviderType>(providerString, true, out var provider)) return provider;

            _logger.LogWarning("The value '{ProviderString}' in SCRIBE_WI_PROVIDER is not a valid provider.", providerString);
            return WorkItemProviderType.Unknown;
        }
    }
}