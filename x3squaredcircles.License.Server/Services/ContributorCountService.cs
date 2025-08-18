using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.License.Server.Data;
using x3squaredcircles.License.Server.Models;

namespace x3squaredcircles.License.Server.Services
{
    /// <summary>
    /// A background service that runs once every 24 hours to fetch and record the
    /// total number of unique contributors from the customer's CI/CD platform.
    /// This provides the data for the annual "true-up" of per-seat licenses.
    /// </summary>
    public class ContributorCountService : IHostedService, IDisposable
    {
        private readonly ILogger<ContributorCountService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private Timer? _timer;

        // Configuration for the CI/CD platform API, loaded from environment variables.
        private readonly string _platformUrl;
        private readonly string _platformPat;

        public ContributorCountService(ILogger<ContributorCountService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            // --- CRITICAL CONFIGURATION ---
            // These must be provided by the customer during deployment via environment variables.
            // This is documented as a prerequisite for the LC to function correctly.
            _platformUrl = Environment.GetEnvironmentVariable("CICD_PLATFORM_URL") ?? string.Empty;
            _platformPat = Environment.GetEnvironmentVariable("CICD_PLATFORM_PAT") ?? string.Empty;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Contributor Count Service is starting.");

            if (string.IsNullOrEmpty(_platformUrl) || string.IsNullOrEmpty(_platformPat))
            {
                _logger.LogCritical("FATAL: CICD_PLATFORM_URL and CICD_PLATFORM_PAT environment variables must be set. Contributor counting will be disabled.");
                // We do not throw here, allowing the concurrency part of the service to function,
                // but this critical failure will be logged.
                return Task.CompletedTask;
            }

            // Run the check immediately on startup to handle container restarts,
            // then schedule it to run every 24 hours.
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromHours(24));

            return Task.CompletedTask;
        }

        private async void DoWork(object? state)
        {
            _logger.LogInformation("Contributor Count Service is running its daily check.");

            // We create a new scope to get a fresh DbContext instance for this background task.
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();
                var todayString = DateTime.UtcNow.ToString("yyyy-MM-dd");

                try
                {
                    // Check if we have already recorded the count for today. This handles container restarts gracefully.
                    var alreadyExists = await dbContext.DailyContributorHistories.AnyAsync(h => h.Date == todayString);
                    if (alreadyExists)
                    {
                        _logger.LogInformation("Contributor count for {Today} has already been recorded. Skipping.", todayString);
                        return;
                    }

                    // Fetch the count from the CI/CD platform API.
                    int contributorCount = await GetContributorCountFromPlatformAsync();

                    // Store the new record in our local database.
                    var newRecord = new DailyContributorHistory
                    {
                        Date = todayString,
                        ContributorCount = contributorCount
                    };

                    dbContext.DailyContributorHistories.Add(newRecord);
                    await dbContext.SaveChangesAsync();

                    _logger.LogInformation("Successfully recorded contributor count for {Today}: {Count} users.", todayString, contributorCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch and record the daily contributor count.");
                }
            }
        }

        private async Task<int> GetContributorCountFromPlatformAsync()
        {
            // This implementation is a production-ready example for Azure DevOps.
            // This can be abstracted with a factory pattern to support other providers like GitHub/GitLab.
            _logger.LogInformation("Fetching contributor count from Azure DevOps: {PlatformUrl}", _platformUrl);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{_platformPat}")));

            // This Azure DevOps Graph API gets users with access levels (e.g., Stakeholder, Basic, etc.)
            // We count anyone with "Basic" access or higher as a billable contributor.
            var requestUrl = $"{_platformUrl}/_apis/graph/users?api-version=6.0-preview.1";

            var response = await client.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(jsonResponse);

            // This logic is specific to the ADO Graph API response structure.
            var users = jsonDoc.RootElement.GetProperty("value").EnumerateArray();
            int contributorCount = users.Count(user =>
            {
                // Gracefully handle cases where the property might be missing.
                if (user.TryGetProperty("accessLevel", out var accessLevelElement) &&
                    accessLevelElement.TryGetProperty("licensingSource", out var licensingSourceElement))
                {
                    var accessLevel = licensingSourceElement.GetString();
                    // "stakeholder" is typically a free, non-contributing user. All others are counted.
                    return !string.Equals(accessLevel, "stakeholder", StringComparison.OrdinalIgnoreCase);
                }
                // If the properties are missing, we default to not counting them to be safe.
                return false;
            });

            _logger.LogInformation("API call successful. Found {Count} contributors.", contributorCount);
            return contributorCount;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Contributor Count Service is stopping.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}