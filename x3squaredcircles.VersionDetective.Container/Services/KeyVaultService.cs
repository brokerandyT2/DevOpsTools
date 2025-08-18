using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.VersionDetective.Container.Models;

namespace x3squaredcircles.VersionDetective.Container.Services
{
    public interface IKeyVaultService
    {
        Task ResolveSecretsAsync(VersionDetectiveConfiguration config);
        Task<string?> GetSecretAsync(string secretName, KeyVaultConfiguration vaultConfig);
    }

    public class KeyVaultService : IKeyVaultService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<KeyVaultService> _logger;

        public KeyVaultService(HttpClient httpClient, ILogger<KeyVaultService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task ResolveSecretsAsync(VersionDetectiveConfiguration config)
        {
            if (config.KeyVault == null)
            {
                _logger.LogDebug("No key vault configuration - skipping secret resolution");
                return;
            }

            try
            {
                _logger.LogInformation("Resolving secrets from {VaultType} key vault", config.KeyVault.Type.ToUpperInvariant());

                // Resolve PAT token if secret name is specified
                if (!string.IsNullOrEmpty(config.Authentication.PatSecretName))
                {
                    var patToken = await GetSecretAsync(config.Authentication.PatSecretName, config.KeyVault);
                    if (!string.IsNullOrEmpty(patToken))
                    {
                        config.Authentication.PatToken = patToken;
                        _logger.LogInformation("✓ Resolved PAT token from secret: {SecretName}", config.Authentication.PatSecretName);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to resolve PAT token from secret: {SecretName}", config.Authentication.PatSecretName);
                    }
                }

                // Could add more secret resolution logic here for other config values
                _logger.LogInformation("✓ Secret resolution completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Key vault secret resolution failed");
                throw new VersionDetectiveException(VersionDetectiveExitCode.KeyVaultAccessFailure,
                    $"Key vault access failed: {ex.Message}", ex);
            }
        }

        public async Task<string?> GetSecretAsync(string secretName, KeyVaultConfiguration vaultConfig)
        {
            if (string.IsNullOrEmpty(secretName) || vaultConfig == null)
            {
                return null;
            }

            try
            {
                return vaultConfig.Type.ToLowerInvariant() switch
                {
                    "azure" => await GetAzureKeyVaultSecretAsync(secretName, vaultConfig),
                    "aws" => await GetAwsSecretsManagerSecretAsync(secretName, vaultConfig),
                    "hashicorp" => await GetHashiCorpVaultSecretAsync(secretName, vaultConfig),
                    _ => throw new VersionDetectiveException(VersionDetectiveExitCode.KeyVaultAccessFailure,
                        $"Unsupported key vault type: {vaultConfig.Type}")
                };
            }
            catch (VersionDetectiveException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get secret {SecretName} from {VaultType}", secretName, vaultConfig.Type);
                throw new VersionDetectiveException(VersionDetectiveExitCode.KeyVaultAccessFailure,
                    $"Failed to retrieve secret {secretName}: {ex.Message}", ex);
            }
        }

        private async Task<string?> GetAzureKeyVaultSecretAsync(string secretName, KeyVaultConfiguration vaultConfig)
        {
            try
            {
                _logger.LogDebug("Getting secret from Azure Key Vault: {SecretName}", secretName);

                // Get access token first
                var accessToken = await GetAzureAccessTokenAsync(vaultConfig);
                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new Exception("Failed to obtain Azure access token");
                }

                // Call Key Vault API
                var vaultUrl = vaultConfig.Url.TrimEnd('/');
                var secretUrl = $"{vaultUrl}/secrets/{secretName}?api-version=7.4";

                using var request = new HttpRequestMessage(HttpMethod.Get, secretUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Azure Key Vault API error: {response.StatusCode} - {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var secretResponse = JsonSerializer.Deserialize<AzureKeyVaultSecretResponse>(responseContent);

                return secretResponse?.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure Key Vault secret retrieval failed");
                throw;
            }
        }

        private async Task<string?> GetAzureAccessTokenAsync(KeyVaultConfiguration vaultConfig)
        {
            try
            {
                var clientId = vaultConfig.Parameters.GetValueOrDefault("ClientId", "");
                var clientSecret = vaultConfig.Parameters.GetValueOrDefault("ClientSecret", "");
                var tenantId = vaultConfig.Parameters.GetValueOrDefault("TenantId", "");

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tenantId))
                {
                    throw new Exception("Missing Azure authentication parameters: ClientId, ClientSecret, or TenantId");
                }

                var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

                var tokenRequest = new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = "https://vault.azure.net/.default"
                };

                using var content = new FormUrlEncodedContent(tokenRequest);
                using var response = await _httpClient.PostAsync(tokenUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Azure token request failed: {response.StatusCode} - {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<AzureTokenResponse>(responseContent);

                return tokenResponse?.AccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure access token retrieval failed");
                throw;
            }
        }

        private async Task<string?> GetAwsSecretsManagerSecretAsync(string secretName, KeyVaultConfiguration vaultConfig)
        {
            try
            {
                _logger.LogDebug("Getting secret from AWS Secrets Manager: {SecretName}", secretName);

                var region = vaultConfig.Parameters.GetValueOrDefault("Region", "us-east-1");
                var accessKeyId = vaultConfig.Parameters.GetValueOrDefault("AccessKeyId", "");
                var secretAccessKey = vaultConfig.Parameters.GetValueOrDefault("SecretAccessKey", "");

                if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(secretAccessKey))
                {
                    throw new Exception("Missing AWS credentials: AccessKeyId or SecretAccessKey");
                }

                // This is a simplified implementation - in production you'd use AWS SDK
                // For now, we'll just log that AWS support is needed
                _logger.LogWarning("AWS Secrets Manager integration requires AWS SDK - not implemented in this simplified version");

                // Return null to indicate secret not retrieved
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AWS Secrets Manager secret retrieval failed");
                throw;
            }
        }

        private async Task<string?> GetHashiCorpVaultSecretAsync(string secretName, KeyVaultConfiguration vaultConfig)
        {
            try
            {
                _logger.LogDebug("Getting secret from HashiCorp Vault: {SecretName}", secretName);

                var token = vaultConfig.Parameters.GetValueOrDefault("Token", "");
                if (string.IsNullOrEmpty(token))
                {
                    throw new Exception("Missing HashiCorp Vault token");
                }

                var vaultUrl = vaultConfig.Url.TrimEnd('/');
                var secretUrl = $"{vaultUrl}/v1/secret/data/{secretName}";

                using var request = new HttpRequestMessage(HttpMethod.Get, secretUrl);
                request.Headers.Add("X-Vault-Token", token);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"HashiCorp Vault API error: {response.StatusCode} - {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var vaultResponse = JsonSerializer.Deserialize<HashiCorpVaultResponse>(responseContent);

                // HashiCorp Vault returns nested data structure
                if (vaultResponse?.Data?.Data != null && vaultResponse.Data.Data.TryGetValue("value", out var secretValue))
                {
                    return secretValue?.ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HashiCorp Vault secret retrieval failed");
                throw;
            }
        }

        // Response models for different vault providers
        private class AzureKeyVaultSecretResponse
        {
            public string? Value { get; set; }
            public string? Id { get; set; }
            public Dictionary<string, object>? Attributes { get; set; }
        }

        private class AzureTokenResponse
        {
            public string? AccessToken { get; set; }
            public string? TokenType { get; set; }
            public int ExpiresIn { get; set; }
        }

        private class HashiCorpVaultResponse
        {
            public HashiCorpVaultData? Data { get; set; }
        }

        private class HashiCorpVaultData
        {
            public Dictionary<string, object>? Data { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }
    }
}