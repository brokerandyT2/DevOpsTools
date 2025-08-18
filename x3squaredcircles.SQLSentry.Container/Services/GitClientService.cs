using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.SQLSentry.Container.Models;

namespace x3squaredcircles.SQLSentry.Container.Services
{
    /// <summary>
    /// Defines the contract for a service that interacts with a remote Git repository.
    /// </summary>
    public interface IGitClientService
    {
        /// <summary>
        /// Downloads the content of a specific file from the configured Git repository.
        /// </summary>
        /// <param name="config">The application configuration containing repository details.</param>
        /// <param name="filePath">The path to the file within the repository.</param>
        /// <returns>The string content of the file.</returns>
        Task<string> DownloadFileContentAsync(GuardianConfiguration config, string filePath);
    }

    /// <summary>
    /// Implements file download functionality for remote Git repositories using their REST APIs.
    /// Currently supports GitHub and Azure DevOps.
    /// </summary>
    public class GitClientService : IGitClientService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<GitClientService> _logger;

        public GitClientService(IHttpClientFactory httpClientFactory, ILogger<GitClientService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<string> DownloadFileContentAsync(GuardianConfiguration config, string filePath)
        {
            _logger.LogInformation("Downloading file from Git repository: {FilePath}", filePath);

            if (string.IsNullOrWhiteSpace(filePath) || filePath == "./")
            {
                _logger.LogWarning("File path '{FilePath}' is invalid or not specified. Skipping download.", filePath);
                return string.Empty;
            }

            try
            {
                var repoUri = new Uri(config.GitRepoUrl);
                var client = _httpClientFactory.CreateClient();
                string apiUrl;
                string branch = "main"; // Default branch, can be expanded later if needed

                // Determine the Git provider and construct the appropriate API URL
                if (repoUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    // GitHub API URL format: https://api.github.com/repos/{owner}/{repo}/contents/{path}
                    var pathSegments = repoUri.AbsolutePath.Trim('/').Split('/');
                    if (pathSegments.Length < 2) throw new ArgumentException("Invalid GitHub repository URL format.");
                    var owner = pathSegments[0];
                    var repo = pathSegments[1].Replace(".git", "");
                    apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{filePath.TrimStart('/')}?ref={branch}";

                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("3SC-Guardian", "1.0"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", config.GitPat);
                }
                else if (repoUri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
                {
                    // Azure DevOps API URL format: https://dev.azure.com/{org}/{project}/_apis/git/repositories/{repo}/items?path={path}
                    apiUrl = $"{config.GitRepoUrl.Replace(".git", "")}/items?path={filePath.TrimStart('/')}&api-version=6.0";

                    var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{config.GitPat}"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
                }
                else
                {
                    throw new NotSupportedException($"Git provider for host '{repoUri.Host}' is not supported.");
                }

                _logger.LogDebug("Constructed Git API URL: {ApiUrl}", apiUrl);

                var response = await client.GetAsync(apiUrl);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("File not found in repository at path '{FilePath}'. Returning empty content.", filePath);
                    return string.Empty;
                }

                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                return await ParseApiResponse(responseContent, repoUri.Host);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download file '{FilePath}' from Git.", filePath);
                throw new GuardianException(
                    ExitCode.GitConnectionFailed,
                    "GIT_DOWNLOAD_FAILED",
                    $"Failed to download file '{filePath}' from the repository. Check URL, path, and PAT.",
                    ex);
            }
        }

        private async Task<string> ParseApiResponse(string jsonContent, string host)
        {
            using var jsonDoc = JsonDocument.Parse(jsonContent);

            if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!jsonDoc.RootElement.TryGetProperty("content", out var contentElement))
                {
                    throw new InvalidDataException("GitHub API response is missing the 'content' field.");
                }
                var base64Content = contentElement.GetString()!.Replace("\n", "");
                var contentBytes = Convert.FromBase64String(base64Content);
                return Encoding.UTF8.GetString(contentBytes);
            }
            else if (host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                // For Azure DevOps, the raw content is returned directly for items
                return jsonContent;
            }

            throw new NotSupportedException($"Response parsing for host '{host}' is not supported.");
        }
    }
}