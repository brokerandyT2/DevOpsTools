using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.SQLSentry.Container.Models;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace x3squaredcircles.SQLSentry.Container.Services
{
    /// <summary>
    // Defines the contract for a service that resolves secrets from an external vault.
    /// </summary>
    public interface IKeyVaultService
    {
        /// <summary>
        /// Resolves the database connection string using the configured vault provider and key.
        /// </summary>
        /// <param name="config">The application configuration containing vault details.</param>
        /// <returns>The resolved database connection string.</returns>
        Task<string> ResolveDbConnectionStringAsync(GuardianConfiguration config);
    }

    /// <summary>
    /// Implements secret resolution for various vault providers like Azure Key Vault, AWS Secrets Manager, and HashiCorp Vault.
    /// </summary>
    public class KeyVaultService : IKeyVaultService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<KeyVaultService> _logger;

        public KeyVaultService(IHttpClientFactory httpClientFactory, ILogger<KeyVaultService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<string> ResolveDbConnectionStringAsync(GuardianConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.DbVaultKey) || string.IsNullOrWhiteSpace(config.VaultProvider))
            {
                throw new GuardianException(ExitCode.InvalidConfiguration, "VAULT_CONFIG_INCOMPLETE", "Vault key and provider must be specified for vault-based connection resolution.");
            }

            _logger.LogInformation("Resolving database connection string from {Provider} using key: {Key}", config.VaultProvider.ToUpper(), config.DbVaultKey);

            return config.VaultProvider.ToLowerInvariant() switch
            {
                "azure" => await ResolveFromAzureKeyVaultAsync(config),
                "aws" => await ResolveFromAwsSecretsManagerAsync(config),
                "hashicorp" => await ResolveFromHashiCorpVaultAsync(config),
                _ => throw new GuardianException(ExitCode.InvalidConfiguration, "UNSUPPORTED_VAULT_PROVIDER", $"The vault provider '{config.VaultProvider}' is not supported.")
            };
        }

        private async Task<string> ResolveFromAzureKeyVaultAsync(GuardianConfiguration config)
        {
            try
            {
                var vaultUri = new Uri(config.VaultUrl ?? throw new ArgumentNullException(nameof(config.VaultUrl)));
                var client = new SecretClient(vaultUri, new DefaultAzureCredential());

                var secret = await client.GetSecretAsync(config.DbVaultKey);

                if (string.IsNullOrWhiteSpace(secret?.Value?.Value))
                {
                    throw new KeyNotFoundException("Secret was retrieved but its value is empty.");
                }

                _logger.LogInformation("✓ Successfully resolved secret from Azure Key Vault.");
                return secret.Value.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve secret from Azure Key Vault.");
                throw new GuardianException(ExitCode.DatabaseConnectionFailed, "AZURE_VAULT_ERROR", $"Failed to retrieve secret '{config.DbVaultKey}' from Azure Key Vault.", ex);
            }
        }

        private async Task<string> ResolveFromAwsSecretsManagerAsync(GuardianConfiguration config)
        {
            try
            {
                // The AWS SDK's default constructor will automatically use the credential chain
                // (e.g., IAM role from EC2 instance or ECS task).
                using var client = new AmazonSecretsManagerClient();
                var request = new GetSecretValueRequest { SecretId = config.DbVaultKey };

                var response = await client.GetSecretValueAsync(request);

                if (string.IsNullOrWhiteSpace(response?.SecretString))
                {
                    throw new KeyNotFoundException("Secret was retrieved but its value is empty.");
                }

                _logger.LogInformation("✓ Successfully resolved secret from AWS Secrets Manager.");
                return response.SecretString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve secret from AWS Secrets Manager.");
                throw new GuardianException(ExitCode.DatabaseConnectionFailed, "AWS_VAULT_ERROR", $"Failed to retrieve secret '{config.DbVaultKey}' from AWS Secrets Manager.", ex);
            }
        }

        private async Task<string> ResolveFromHashiCorpVaultAsync(GuardianConfiguration config)
        {
            try
            {
                var vaultToken = Environment.GetEnvironmentVariable("THREE_SC_VAULT_TOKEN");
                if (string.IsNullOrWhiteSpace(vaultToken))
                {
                    throw new GuardianException(ExitCode.InvalidConfiguration, "MISSING_VAULT_TOKEN", "Environment variable 'THREE_SC_VAULT_TOKEN' is required for HashiCorp Vault.");
                }

                // Standard KVv2 API path format
                var apiUrl = new Uri(new Uri(config.VaultUrl!), $"v1/secret/data/{config.DbVaultKey}");

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", vaultToken);

                var response = await client.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(jsonContent);

                if (!jsonDoc.RootElement.TryGetProperty("data", out var dataElement) ||
                    !dataElement.TryGetProperty("data", out var innerDataElement))
                {
                    throw new InvalidDataException("HashiCorp Vault response did not contain the expected 'data.data' structure for a KVv2 secret.");
                }

                // Look for a common key like 'value' or 'connectionString', otherwise fail.
                if (innerDataElement.TryGetProperty("connectionString", out var connStringElement))
                {
                    _logger.LogInformation("✓ Successfully resolved secret from HashiCorp Vault.");
                    return connStringElement.GetString()!;
                }
                if (innerDataElement.TryGetProperty("value", out var valueElement))
                {
                    _logger.LogInformation("✓ Successfully resolved secret from HashiCorp Vault.");
                    return valueElement.GetString()!;
                }

                throw new KeyNotFoundException("Secret data was retrieved but did not contain a 'connectionString' or 'value' field.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve secret from HashiCorp Vault.");
                throw new GuardianException(ExitCode.DatabaseConnectionFailed, "HASHICORP_VAULT_ERROR", $"Failed to retrieve secret '{config.DbVaultKey}' from HashiCorp Vault.", ex);
            }
        }
    }
}