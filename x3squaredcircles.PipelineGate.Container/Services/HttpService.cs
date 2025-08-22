using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using x3squaredcircles.PipelineGate.Container.Models;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    public class HttpService : IHttpService
    {
        private readonly ILogger<HttpService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IKeyVaultService _keyVaultService;

        public HttpService(
            ILogger<HttpService> logger,
            IHttpClientFactory httpClientFactory,
            IKeyVaultService keyVaultService)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _keyVaultService = keyVaultService;
        }

        public async Task NotifyAsync(string url, string payload)
        {
            try
            {
                _logger.LogInformation("Sending NOTIFY request to {Url}", url);
                var client = _httpClientFactory.CreateClient("GateClient");
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                // Fire and forget, but log any exceptions
                _ = client.PostAsync(url, content).ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        _logger.LogWarning(task.Exception?.GetBaseException(), "Notify request to {Url} failed.", url);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initiate Notify request to {Url}.", url);
            }
            await Task.CompletedTask;
        }

        public async Task<HttpResponseMessage> SendRequestAsync(string url, string secretName = null)
        {
            try
            {
                _logger.LogDebug("Sending GATE request to {Url}", url);
                var client = _httpClientFactory.CreateClient("GateClient");
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (!string.IsNullOrWhiteSpace(secretName))
                {
                    var token = await _keyVaultService.GetSecretAsync(secretName);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        _logger.LogDebug("Attached Bearer token from secret '{SecretName}'.", secretName);
                    }
                    else
                    {
                        throw new PipelineGateException(GateExitCode.ApiConnectionFailure, $"Secret '{secretName}' was resolved but returned an empty value.");
                    }
                }

                return await client.SendAsync(request);
            }
            catch (PipelineGateException) { throw; } // Re-throw our specific exceptions
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "API connection failure for URL {Url}.", url);
                throw new PipelineGateException(GateExitCode.ApiConnectionFailure, $"Failed to connect to the endpoint: {url}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "API request to {Url} timed out.", url);
                throw new PipelineGateException(GateExitCode.Timeout, $"Request to the endpoint timed out: {url}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while sending a request to {Url}.", url);
                throw new PipelineGateException(GateExitCode.ApiConnectionFailure, $"An unexpected error occurred connecting to {url}", ex);
            }
        }
    }
}