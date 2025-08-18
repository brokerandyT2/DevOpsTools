using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.SQLSync.Generator.Models;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace x3squaredcircles.SQLSync.Generator.Services
{
    public interface IKeyVaultService
    {
        Task ResolveSecretsAsync(SqlSchemaConfiguration config);
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

        public async Task ResolveSecretsAsync(SqlSchemaConfiguration config)
        {
            if (config.KeyVault == null)
            {
                _logger.LogDebug("No key vault configuration provided, skipping secret resolution");
                return;
            }

            try
            {
                _logger.LogInformation("Resolving secrets from {VaultType} key vault: {VaultUrl}",
                    config.KeyVault.Type.ToUpperInvariant(), config.KeyVault.Url);

                var vaultProvider = CreateVaultProvider(config.KeyVault);
                await vaultProvider.ResolveSecretsAsync(config);

                _logger.LogInformation("✓ Secrets resolved successfully from key vault");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve secrets from key vault");
                throw new SqlSchemaException(SqlSchemaExitCode.KeyVaultAccessFailure,
                    $"Failed to resolve secrets from key vault: {ex.Message}", ex);
            }
        }

        private IKeyVaultProvider CreateVaultProvider(KeyVaultConfiguration keyVaultConfig)
        {
            return keyVaultConfig.Type.ToLowerInvariant() switch
            {
                "azure" => new AzureKeyVaultProvider(_logger),
                "aws" => new AwsSecretsManagerProvider(_logger),
                "hashicorp" => new HashiCorpVaultProvider(_httpClient, _logger),
                _ => throw new SqlSchemaException(SqlSchemaExitCode.InvalidConfiguration,
                    $"Unsupported key vault type: {keyVaultConfig.Type}")
            };
        }
    }

    public interface IKeyVaultProvider
    {
        Task ResolveSecretsAsync(SqlSchemaConfiguration config);
    }

    // Azure Key Vault Provider
    public class AzureKeyVaultProvider : IKeyVaultProvider
    {
        private readonly ILogger _logger;

        public AzureKeyVaultProvider(ILogger logger)
        {
            _logger = logger;
        }

        public async Task ResolveSecretsAsync(SqlSchemaConfiguration config)
        {
            var keyVaultConfig = config.KeyVault!;

            if (string.IsNullOrEmpty(keyVaultConfig.Url))
            {
                throw new ArgumentException("Azure Key Vault URL is required");
            }

            SecretClient client;

            // Try different authentication methods
            if (keyVaultConfig.Parameters.ContainsKey("ClientId") &&
                keyVaultConfig.Parameters.ContainsKey("ClientSecret") &&
                keyVaultConfig.Parameters.ContainsKey("TenantId"))
            {
                // Service Principal authentication
                var credential = new ClientSecretCredential(
                    keyVaultConfig.Parameters["TenantId"],
                    keyVaultConfig.Parameters["ClientId"],
                    keyVaultConfig.Parameters["ClientSecret"]);

                client = new SecretClient(new Uri(keyVaultConfig.Url), credential);
                _logger.LogDebug("Using Azure service principal authentication");
            }
            else
            {
                // Managed Identity or Azure CLI authentication
                var credential = new DefaultAzureCredential();
                client = new SecretClient(new Uri(keyVaultConfig.Url), credential);
                _logger.LogDebug("Using Azure default credential authentication");
            }

            // Resolve database credentials
            await ResolveAzureDatabaseCredentialsAsync(client, config);

            // Resolve authentication tokens
            await ResolveAzureAuthenticationTokensAsync(client, config);
        }

        private async Task ResolveAzureDatabaseCredentialsAsync(SecretClient client, SqlSchemaConfiguration config)
        {
            // Database username
            if (!string.IsNullOrEmpty(config.Database.UsernameVaultKey))
            {
                try
                {
                    var usernameSecret = await client.GetSecretAsync(config.Database.UsernameVaultKey);
                    config.Database.Username = usernameSecret.Value.Value;
                    _logger.LogDebug("Resolved database username from vault key: {VaultKey}", config.Database.UsernameVaultKey);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to retrieve database username from vault key '{config.Database.UsernameVaultKey}': {ex.Message}", ex);
                }
            }

            // Database password
            if (!string.IsNullOrEmpty(config.Database.PasswordVaultKey))
            {
                try
                {
                    var passwordSecret = await client.GetSecretAsync(config.Database.PasswordVaultKey);
                    config.Database.Password = passwordSecret.Value.Value;
                    _logger.LogDebug("Resolved database password from vault key: {VaultKey}", config.Database.PasswordVaultKey);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to retrieve database password from vault key '{config.Database.PasswordVaultKey}': {ex.Message}", ex);
                }
            }
        }

        private async Task ResolveAzureAuthenticationTokensAsync(SecretClient client, SqlSchemaConfiguration config)
        {
            // PAT token
            if (!string.IsNullOrEmpty(config.Authentication.PatSecretName))
            {
                try
                {
                    var patSecret = await client.GetSecretAsync(config.Authentication.PatSecretName);
                    config.Authentication.PatToken = patSecret.Value.Value;
                    _logger.LogDebug("Resolved PAT token from vault key: {VaultKey}", config.Authentication.PatSecretName);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to retrieve PAT token from vault key '{config.Authentication.PatSecretName}': {ex.Message}", ex);
                }
            }
        }
    }

    // AWS Secrets Manager Provider
    public class AwsSecretsManagerProvider : IKeyVaultProvider
    {
        private readonly ILogger _logger;

        public AwsSecretsManagerProvider(ILogger logger)
        {
            _logger = logger;
        }

        public async Task ResolveSecretsAsync(SqlSchemaConfiguration config)
        {
            var keyVaultConfig = config.KeyVault!;

            var clientConfig = new AmazonSecretsManagerConfig();

            // Set region
            if (keyVaultConfig.Parameters.ContainsKey("Region"))
            {
                clientConfig.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(keyVaultConfig.Parameters["Region"]);
            }

            AmazonSecretsManagerClient client;

            // Try different authentication methods
            if (keyVaultConfig.Parameters.ContainsKey("AccessKeyId") &&
                keyVaultConfig.Parameters.ContainsKey("SecretAccessKey"))
            {
                // Explicit credentials
                client = new AmazonSecretsManagerClient(
                    keyVaultConfig.Parameters["AccessKeyId"],
                    keyVaultConfig.Parameters["SecretAccessKey"],
                    clientConfig);
                _logger.LogDebug("Using AWS explicit credentials authentication");
            }
            else
            {
                // Default credential chain (IAM role, environment variables, etc.)
                client = new AmazonSecretsManagerClient(clientConfig);
                _logger.LogDebug("Using AWS default credential chain authentication");
            }

            using (client)
            {
                // Resolve database credentials
                await ResolveAwsDatabaseCredentialsAsync(client, config);

                // Resolve authentication tokens
                await ResolveAwsAuthenticationTokensAsync(client, config);
            }
        }

        private async Task ResolveAwsDatabaseCredentialsAsync(AmazonSecretsManagerClient client, SqlSchemaConfiguration config)
        {
            // Database username
            if (!string.IsNullOrEmpty(config.Database.UsernameVaultKey))
            {
                try
                {
                    var request = new GetSecretValueRequest
                    {
                        SecretId = config.Database.UsernameVaultKey
                    };
                    var response = await client.GetSecretValueAsync(request);
                    config.Database.Username = response.SecretString;
                    _logger.LogDebug("Resolved database username from AWS secret: {SecretId}", config.Database.UsernameVaultKey);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to retrieve database username from AWS secret '{config.Database.UsernameVaultKey}': {ex.Message}", ex);
                }
            }

            // Database password
            if (!string.IsNullOrEmpty(config.Database.PasswordVaultKey))
            {
                try
                {
                    var request = new GetSecretValueRequest
                    {
                        SecretId = config.Database.PasswordVaultKey
                    };
                    var response = await client.GetSecretValueAsync(request);

                    // Handle both simple string secrets and JSON secrets
                    if (response.SecretString.StartsWith("{"))
                    {
                        // JSON secret - parse to get the password field
                        var secretJson = JsonSerializer.Deserialize<Dictionary<string, object>>(response.SecretString);
                        if (secretJson != null && secretJson.ContainsKey("password"))
                        {
                            config.Database.Password = secretJson["password"].ToString();
                        }
                        else
                        {
                            throw new InvalidOperationException($"JSON secret '{config.Database.PasswordVaultKey}' does not contain 'password' field");
                        }
                    }
                    else
                    {
                        // Simple string secret
                        config.Database.Password = response.SecretString;
                    }

                    _logger.LogDebug("Resolved database password from AWS secret: {SecretId}", config.Database.PasswordVaultKey);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to retrieve database password from AWS secret '{config.Database.PasswordVaultKey}': {ex.Message}", ex);
                }
            }
        }

        private async Task ResolveAwsAuthenticationTokensAsync(AmazonSecretsManagerClient client, SqlSchemaConfiguration config)
        {
            // PAT token
            if (!string.IsNullOrEmpty(config.Authentication.PatSecretName))
            {
                try
                {
                    var request = new GetSecretValueRequest
                    {
                        SecretId = config.Authentication.PatSecretName
                    };
                    var response = await client.GetSecretValueAsync(request);
                    config.Authentication.PatToken = response.SecretString;
                    _logger.LogDebug("Resolved PAT token from AWS secret: {SecretId}", config.Authentication.PatSecretName);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to retrieve PAT token from AWS secret '{config.Authentication.PatSecretName}': {ex.Message}", ex);
                }
            }
        }
    }

    // HashiCorp Vault Provider
    public class HashiCorpVaultProvider : IKeyVaultProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public HashiCorpVaultProvider(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task ResolveSecretsAsync(SqlSchemaConfiguration config)
        {
            var keyVaultConfig = config.KeyVault!;

            if (string.IsNullOrEmpty(keyVaultConfig.Url))
            {
                throw new ArgumentException("HashiCorp Vault URL is required");
            }

            if (!keyVaultConfig.Parameters.ContainsKey("Token") || string.IsNullOrEmpty(keyVaultConfig.Parameters["Token"]))
            {
                throw new ArgumentException("HashiCorp Vault token is required");
            }

            var token = keyVaultConfig.Parameters["Token"];

            // Test vault connection and authentication
            await TestVaultConnectionAsync(keyVaultConfig.Url, token);

            // Resolve database credentials
            await ResolveVaultDatabaseCredentialsAsync(keyVaultConfig.Url, token, config);

            // Resolve authentication tokens
            await ResolveVaultAuthenticationTokensAsync(keyVaultConfig.Url, token, config);
        }

        private async Task TestVaultConnectionAsync(string vaultUrl, string token)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{vaultUrl.TrimEnd('/')}/v1/auth/token/lookup-self");
                request.Headers.Add("X-Vault-Token", token);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    throw new InvalidOperationException($"Vault authentication failed: {response.StatusCode} - {content}");
                }

                _logger.LogDebug("HashiCorp Vault authentication successful");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to authenticate with HashiCorp Vault: {ex.Message}", ex);
            }
        }

        private async Task ResolveVaultDatabaseCredentialsAsync(string vaultUrl, string token, SqlSchemaConfiguration config)
        {
            // Database username
            if (!string.IsNullOrEmpty(config.Database.UsernameVaultKey))
            {
                try
                {
                    var username = await GetVaultSecretAsync(vaultUrl, token, config.Database.UsernameVaultKey);
                    config.Database.Username = username;
                    _logger.LogDebug("Resolved database username from Vault path: {VaultPath}", config.Database.UsernameVaultKey);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to retrieve database username from Vault path '{config.Database.UsernameVaultKey}': {ex.Message}", ex);
                }
            }

            // Database password
            if (!string.IsNullOrEmpty(config.Database.PasswordVaultKey))
            {
                try
                {
                    var password = await GetVaultSecretAsync(vaultUrl, token, config.Database.PasswordVaultKey);
                    config.Database.Password = password;
                    _logger.LogDebug("Resolved database password from Vault path: {VaultPath}", config.Database.PasswordVaultKey);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to retrieve database password from Vault path '{config.Database.PasswordVaultKey}': {ex.Message}", ex);
                }
            }
        }

        private async Task ResolveVaultAuthenticationTokensAsync(string vaultUrl, string token, SqlSchemaConfiguration config)
        {
            // PAT token
            if (!string.IsNullOrEmpty(config.Authentication.PatSecretName))
            {
                try
                {
                    var patToken = await GetVaultSecretAsync(vaultUrl, token, config.Authentication.PatSecretName);
                    config.Authentication.PatToken = patToken;
                    _logger.LogDebug("Resolved PAT token from Vault path: {VaultPath}", config.Authentication.PatSecretName);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to retrieve PAT token from Vault path '{config.Authentication.PatSecretName}': {ex.Message}", ex);
                }
            }
        }

        private async Task<string> GetVaultSecretAsync(string vaultUrl, string token, string secretPath)
        {
            try
            {
                // Support both KV v1 and v2 paths
                var path = secretPath.StartsWith("secret/") ? secretPath : $"secret/{secretPath}";
                var url = $"{vaultUrl.TrimEnd('/')}/v1/{path}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Vault-Token", token);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    // Try KV v2 format if v1 fails
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound && !path.Contains("/data/"))
                    {
                        var kvv2Path = path.Replace("secret/", "secret/data/");
                        url = $"{vaultUrl.TrimEnd('/')}/v1/{kvv2Path}";
                        request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.Add("X-Vault-Token", token);
                        response = await _httpClient.SendAsync(request);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        throw new InvalidOperationException($"Failed to read secret: {response.StatusCode} - {content}");
                    }
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var vaultResponse = JsonSerializer.Deserialize<VaultSecretResponse>(responseContent);

                if (vaultResponse?.Data == null)
                {
                    throw new InvalidOperationException($"Secret not found or has no data: {secretPath}");
                }

                // Handle KV v1 vs v2 response format
                Dictionary<string, object>? secretData;
                if (vaultResponse.Data.ContainsKey("data") && vaultResponse.Data["data"] is JsonElement dataElement)
                {
                    // KV v2 format
                    secretData = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText());
                }
                else
                {
                    // KV v1 format
                    secretData = vaultResponse.Data;
                }

                if (secretData == null || secretData.Count == 0)
                {
                    throw new InvalidOperationException($"Secret contains no data: {secretPath}");
                }

                // Extract the secret value
                // First try common field names
                foreach (var fieldName in new[] { "value", "password", "token", "secret" })
                {
                    if (secretData.ContainsKey(fieldName))
                    {
                        return secretData[fieldName].ToString()!;
                    }
                }

                // If no common field names, return the first value
                var firstValue = secretData.Values.First();
                return firstValue?.ToString() ?? throw new InvalidOperationException($"Secret value is null: {secretPath}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to retrieve secret from HashiCorp Vault path '{secretPath}': {ex.Message}", ex);
            }
        }

        private class VaultSecretResponse
        {
            public Dictionary<string, object>? Data { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }
    }
}