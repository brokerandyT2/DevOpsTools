using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.PipelineGate.Container.Models;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.Runtime;
using Amazon;

namespace x3squaredcircles.PipelineGate.Container.Services
{
    public class KeyVaultService : IKeyVaultService
    {
        private readonly ILogger<KeyVaultService> _logger;
        private readonly GateConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public KeyVaultService(ILogger<KeyVaultService> logger, GateConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<string> GetSecretAsync(string secretName)
        {
            if (_config.Vault.Type == VaultType.None)
            {
                _logger.LogWarning("Key vault is not configured, but a secret '{SecretName}' was requested. Returning null.", secretName);
                return null;
            }

            _logger.LogInformation("Retrieving secret '{SecretName}' from {VaultType} vault...", secretName, _config.Vault.Type);

            try
            {
                return _config.Vault.Type switch
                {
                    VaultType.Azure => await GetAzureKeyVaultSecretAsync(secretName),
                    VaultType.Aws => await GetAwsSecretAsync(secretName),
                    VaultType.HashiCorp => await GetHashiCorpVaultSecretAsync(secretName),
                    _ => throw new PipelineGateException(GateExitCode.InvalidConfiguration, $"Unsupported vault type: '{_config.Vault.Type}'")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve secret '{SecretName}' from vault.", secretName);
                throw new PipelineGateException(GateExitCode.InvalidConfiguration, $"Failed to retrieve secret '{secretName}' from vault.", ex);
            }
        }

        private async Task<string> GetAzureKeyVaultSecretAsync(string secretName)
        {
            if (string.IsNullOrWhiteSpace(_config.Vault.Url))
            {
                throw new PipelineGateException(GateExitCode.InvalidConfiguration, "Azure Key Vault URL is not configured (3SC_VAULT_URL).");
            }

            var vaultUri = new Uri(_config.Vault.Url);
            var credential = new DefaultAzureCredential();
            var client = new SecretClient(vaultUri, credential);

            KeyVaultSecret secret = await client.GetSecretAsync(secretName);

            if (secret?.Value == null)
            {
                throw new PipelineGateException(GateExitCode.InvalidConfiguration, $"Secret '{secretName}' not found or has no value in vault '{vaultUri}'.");
            }

            _logger.LogInformation("✓ Successfully retrieved secret '{SecretName}' from Azure Key Vault.", secretName);
            return secret.Value;
        }

        private async Task<string> GetAwsSecretAsync(string secretName)
        {
            var awsRegion = Environment.GetEnvironmentVariable("AWS_REGION");
            if (string.IsNullOrWhiteSpace(awsRegion))
            {
                throw new PipelineGateException(GateExitCode.InvalidConfiguration, "AWS_REGION environment variable must be set for AWS Secrets Manager.");
            }

            var clientConfig = new AmazonSecretsManagerConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(awsRegion) };

            // The AWS SDK for .NET will automatically use the default credential chain:
            // 1. Environment variables (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY)
            // 2. IAM role for the container/EC2 instance
            using var client = new AmazonSecretsManagerClient(clientConfig);

            var request = new GetSecretValueRequest { SecretId = secretName };
            var response = await client.GetSecretValueAsync(request);

            if (response == null)
            {
                throw new PipelineGateException(GateExitCode.InvalidConfiguration, $"Secret '{secretName}' not found in AWS Secrets Manager.");
            }

            if (response.SecretString != null)
            {
                _logger.LogInformation("✓ Successfully retrieved secret '{SecretName}' from AWS Secrets Manager.", secretName);
                return response.SecretString;
            }

            throw new PipelineGateException(GateExitCode.InvalidConfiguration, $"Secret '{secretName}' in AWS Secrets Manager is a binary secret, which is not supported.");
        }

        private async Task<string> GetHashiCorpVaultSecretAsync(string secretName)
        {
            var vaultUrl = _config.Vault.Url;
            var vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN");

            if (string.IsNullOrWhiteSpace(vaultUrl))
            {
                throw new PipelineGateException(GateExitCode.InvalidConfiguration, "HashiCorp Vault URL is not configured (3SC_VAULT_URL).");
            }
            if (string.IsNullOrWhiteSpace(vaultToken))
            {
                throw new PipelineGateException(GateExitCode.InvalidConfiguration, "VAULT_TOKEN environment variable must be set for HashiCorp Vault.");
            }

            var client = _httpClientFactory.CreateClient("VaultClient");
            client.BaseAddress = new Uri(vaultUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", vaultToken);

            // Assuming KV V2 engine, which is standard. Path is secret/data/{secretName}
            var response = await client.GetAsync($"/v1/secret/data/{secretName}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(content);

            if (jsonDoc.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("data", out var innerDataElement) &&
                innerDataElement.TryGetProperty("value", out var valueElement))
            {
                _logger.LogInformation("✓ Successfully retrieved secret '{SecretName}' from HashiCorp Vault.", secretName);
                return valueElement.GetString();
            }

            throw new PipelineGateException(GateExitCode.InvalidConfiguration, $"Secret '{secretName}' is in an invalid format or does not contain a 'value' field in HashiCorp Vault.");
        }
    }
}