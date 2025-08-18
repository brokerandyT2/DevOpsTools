using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using x3squaredcircles.MobileAdapter.Generator.Configuration;
using x3squaredcircles.MobileAdapter.Generator.Models;

namespace x3squaredcircles.MobileAdapter.Generator.Licensing
{
    /// <summary>
    /// Manages the acquisition and validation of licenses from the central license server.
    /// </summary>
    public class LicenseManager
    {
        private readonly ILogger<LicenseManager> _logger;
        private readonly HttpClient _httpClient;
        private readonly GeneratorConfiguration _config;

        public LicenseManager(ILogger<LicenseManager> logger, IHttpClientFactory httpClientFactory, GeneratorConfiguration config)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _config = config;
        }

        /// <summary>
        /// Validates the license by contacting the license server. Implements a retry mechanism.
        /// </summary>
        /// <returns>A result object indicating whether the license is valid and under what conditions.</returns>
        public async Task<LicenseValidationResult> ValidateLicenseAsync()
        {
            try
            {
                var licenseRequest = new LicenseRequest
                {
                    ToolName = _config.ToolName,
                    RequestedBy = Environment.UserName,
                    RequestTime = DateTime.UtcNow,
                    Repository = _config.RepoUrl,
                    Branch = _config.Branch
                };

                var response = await RequestLicenseWithRetryAsync(licenseRequest);

                if (response == null)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "License server unreachable after all retries."
                    };
                }

                return ProcessLicenseResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A critical error occurred during license validation.");
                return new LicenseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"License validation process failed: {ex.Message}"
                };
            }
        }

        private async Task<LicenseResponse?> RequestLicenseWithRetryAsync(LicenseRequest request)
        {
            var maxRetries = _config.LicenseTimeout / _config.LicenseRetryInterval;
            var currentRetry = 0;

            while (currentRetry <= maxRetries)
            {
                try
                {
                    _logger.LogDebug("License request attempt {CurrentRetry}/{MaxRetries}", currentRetry + 1, maxRetries + 1);

                    var json = JsonSerializer.Serialize(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.LicenseRetryInterval));
                    var response = await _httpClient.PostAsync($"{_config.LicenseServer}/api/license/request", content, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseJson = await response.Content.ReadAsStringAsync();
                        return JsonSerializer.Deserialize<LicenseResponse>(responseJson);
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("Burst capacity exceeded, waiting for an available license slot.");
                    }
                    else
                    {
                        _logger.LogWarning("License server returned an error: {StatusCode} {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);
                    }
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("License request timed out during attempt {CurrentRetry}.", currentRetry + 1);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("License server connection failed: {Message}", ex.Message);
                }

                if (currentRetry < maxRetries)
                {
                    _logger.LogInformation("Retrying license request in {RetryInterval} seconds...", _config.LicenseRetryInterval);
                    await Task.Delay(TimeSpan.FromSeconds(_config.LicenseRetryInterval));
                }

                currentRetry++;
            }

            return null; // All retries failed
        }

        private LicenseValidationResult ProcessLicenseResponse(LicenseResponse response)
        {
            switch (response.Status)
            {
                case LicenseStatus.Valid:
                    _logger.LogInformation("License validated successfully. Session expires at: {ExpiresAt}", response.ExpiresAt);
                    return new LicenseValidationResult
                    {
                        IsValid = true,
                        LicenseId = response.LicenseId,
                        ExpiresAt = response.ExpiresAt
                    };

                case LicenseStatus.Expired:
                    _logger.LogWarning("The license is expired. The tool will run in NO-OP (analysis-only) mode.");
                    return new LicenseValidationResult
                    {
                        IsValid = true, // It's "valid" in the sense that the tool can proceed, but in a limited mode.
                        IsNoOpMode = true,
                        IsExpired = true,
                        ErrorMessage = "License has expired."
                    };

                case LicenseStatus.Unavailable:
                    _logger.LogError("No licenses are currently available, and burst capacity is exhausted.");
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "All concurrent licenses are in use."
                    };

                case LicenseStatus.Invalid:
                    _logger.LogError("License validation failed: {ErrorMessage}", response.ErrorMessage);
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = response.ErrorMessage
                    };

                default:
                    _logger.LogError("Received an unknown license status from the server: {Status}", response.Status);
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Unknown license status: {response.Status}"
                    };
            }
        }
    }

    #region DTOs and Enums

    public class LicenseRequest
    {
        public string ToolName { get; set; }
        public string RequestedBy { get; set; }
        public DateTime RequestTime { get; set; }
        public string Repository { get; set; }
        public string Branch { get; set; }
    }

    public class LicenseResponse
    {
        public LicenseStatus Status { get; set; }
        public string? LicenseId { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public bool IsNoOpMode { get; set; }
        public bool IsExpired { get; set; }
        public string? LicenseId { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public enum LicenseStatus
    {
        Valid,
        Expired,
        Unavailable,
        Invalid
    }

    #endregion
}