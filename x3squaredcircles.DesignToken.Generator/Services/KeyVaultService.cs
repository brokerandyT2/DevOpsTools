using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public interface IKeyVaultService { Task ResolveSecretsAsync(TokensConfiguration config); }

    public class KeyVaultService : IKeyVaultService
    {
        private readonly IAppLogger _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public KeyVaultService(IAppLogger logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task ResolveSecretsAsync(TokensConfiguration config)
        {
            if (string.IsNullOrEmpty(config.KeyVault.Type))
            {
                _logger.LogDebug("No key vault configuration provided. Skipping secret resolution.");
                return;
            }
            try
            {
                _logger.LogStartPhase($"Secret Resolution ({config.KeyVault.Type.ToUpperInvariant()})");
                if (!string.IsNullOrEmpty(config.KeyVault.PatSecretName))
                {
                    var patToken = await GetSecretAsync(config.KeyVault.PatSecretName, config.KeyVault);
                    if (!string.IsNullOrEmpty(patToken))
                    {
                        Environment.SetEnvironmentVariable("DATALINK_PAT_TOKEN", patToken);
                        _logger.LogInfo($"✓ Resolved Git PAT token from secret: {config.KeyVault.PatSecretName}");
                    }
                }
                await ResolveDesignPlatformTokenAsync(config);
                _logger.LogEndPhase("Secret Resolution", true);
            }
            catch (Exception ex)
            {
                _logger.LogError("Key vault secret resolution failed.", ex);
                throw new DesignTokenException(DesignTokenExitCode.UnhandledException, $"Key vault access failed: {ex.Message}", ex);
            }
        }

        private async Task ResolveDesignPlatformTokenAsync(TokensConfiguration config)
        {
            string secretName = string.Empty;
            string envVarName = string.Empty;

            switch (config.DesignPlatform.ToLowerInvariant())
            {
                case "figma":
                    secretName = config.Figma.TokenSecretName;
                    envVarName = "FIGMA_API_TOKEN";
                    break;
                case "sketch":
                    secretName = config.Sketch.TokenSecretName;
                    envVarName = "SKETCH_API_TOKEN";
                    break;
                case "adobe-xd":
                    secretName = config.AdobeXd.TokenSecretName;
                    envVarName = "ADOBE_XD_API_TOKEN";
                    break;
                case "zeplin":
                    secretName = config.Zeplin.TokenSecretName;
                    envVarName = "ZEPLIN_API_TOKEN";
                    break;
                case "abstract":
                    secretName = config.Abstract.TokenSecretName;
                    envVarName = "ABSTRACT_API_TOKEN";
                    break;
                case "penpot":
                    secretName = config.Penpot.TokenSecretName;
                    envVarName = "PENPOT_API_TOKEN";
                    break;
            }

            if (!string.IsNullOrEmpty(secretName) && !string.IsNullOrEmpty(envVarName))
            {
                var token = await GetSecretAsync(secretName, config.KeyVault);
                if (!string.IsNullOrEmpty(token))
                {
                    Environment.SetEnvironmentVariable(envVarName, token);
                    _logger.LogInfo($"✓ Resolved {config.DesignPlatform} API token from secret: {secretName}");
                }
                else
                {
                    _logger.LogWarning($"Could not resolve {config.DesignPlatform} API token from secret '{secretName}'. The tool will proceed, but may fail if the token is not provided via other means.");
                }
            }
        }

        private async Task<string?> GetSecretAsync(string secretName, KeyVaultConfig vaultConfig)
        {
            try
            {
                _logger.LogDebug($"Attempting to retrieve secret '{secretName}' from {vaultConfig.Type} vault.");
                return vaultConfig.Type.ToLowerInvariant() switch
                {
                    "azure" => await GetAzureKeyVaultSecretAsync(secretName, vaultConfig),
                    "aws" => await GetAwsSecretsManagerSecretAsync(secretName, vaultConfig),
                    "hashicorp" => await GetHashiCorpVaultSecretAsync(secretName, vaultConfig),
                    "gcp" => await GetGcpSecretManagerSecretAsync(secretName, vaultConfig),
                    _ => throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration, $"Unsupported key vault type: {vaultConfig.Type}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to get secret '{secretName}' from vault type '{vaultConfig.Type}'.", ex);
                return null;
            }
        }
        #region Private Vault-Specific Implementations

        // --- AZURE IMPLEMENTATION ---
        private async Task<string?> GetAzureKeyVaultSecretAsync(string secretName, KeyVaultConfig config)
        {
            var client = _httpClientFactory.CreateClient("AzureKeyVaultClient");
            var accessToken = await GetAzureAccessTokenAsync(config);
            if (string.IsNullOrEmpty(accessToken)) return null;

            var vaultUrl = config.Url.TrimEnd('/');
            var secretUrl = $"{vaultUrl}/secrets/{secretName}?api-version=7.4";

            using var request = new HttpRequestMessage(HttpMethod.Get, secretUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Azure Key Vault API error ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}", null);
                return null;
            }

            var responseJson = await response.Content.ReadFromJsonAsync<JsonDocument>();
            return responseJson?.RootElement.GetProperty("value").GetString();
        }

        private async Task<string?> GetAzureAccessTokenAsync(KeyVaultConfig config)
        {
            if (string.IsNullOrEmpty(config.AzureClientId) || string.IsNullOrEmpty(config.AzureClientSecret) || string.IsNullOrEmpty(config.AzureTenantId))
            {
                _logger.LogWarning("Missing Azure authentication variables: TOKENS_AZURE_CLIENT_ID, TOKENS_AZURE_CLIENT_SECRET, or TOKENS_AZURE_TENANT_ID.");
                return null;
            }
            var tokenUrl = $"https://login.microsoftonline.com/{config.AzureTenantId}/oauth2/v2.0/token";
            var tokenRequest = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = config.AzureClientId,
                ["client_secret"] = config.AzureClientSecret,
                ["scope"] = "https://vault.azure.net/.default"
            };

            var client = _httpClientFactory.CreateClient("AzureAuthClient");
            using var content = new FormUrlEncodedContent(tokenRequest);
            using var response = await client.PostAsync(tokenUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Azure token request failed ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}", null);
                return null;
            }
            var responseJson = await response.Content.ReadFromJsonAsync<JsonDocument>();
            return responseJson?.RootElement.GetProperty("access_token").GetString();
        }

        // --- AWS IMPLEMENTATION ---
        private async Task<string?> GetAwsSecretsManagerSecretAsync(string secretName, KeyVaultConfig config)
        {
            var client = _httpClientFactory.CreateClient("AwsClient");
            var region = config.AwsRegion;
            if (string.IsNullOrEmpty(region))
            {
                _logger.LogWarning("Missing AWS region (TOKENS_AWS_REGION). Defaulting to us-east-1.");
                region = "us-east-1";
            }

            var endpoint = new Uri($"https://secretsmanager.{region}.amazonaws.com/");
            var requestBody = JsonSerializer.Serialize(new { SecretId = secretName });

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/x-amz-json-1.1")
            };
            request.Headers.Add("X-Amz-Target", "secretsmanager.GetSecretValue");

            // Create an instance of our SigV4 signer and sign the request.
            var signer = new AwsV4Signer(config.AwsAccessKeyId, config.AwsSecretAccessKey, region, "secretsmanager");
            await signer.SignRequest(request);

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"AWS Secrets Manager API error ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}", null);
                return null;
            }
            var responseJson = await response.Content.ReadFromJsonAsync<JsonDocument>();
            return responseJson?.RootElement.GetProperty("SecretString").GetString();
        }


        // --- NEW: Self-contained AWS Signature V4 Helper Class ---
        // This class can be added to the bottom of the KeyVaultService.cs file.
        public class AwsV4Signer
        {
            private readonly string _accessKeyId;
            private readonly string _secretAccessKey;
            private readonly string _region;
            private readonly string _service;

            public AwsV4Signer(string accessKeyId, string secretAccessKey, string region, string service)
            {
                _accessKeyId = accessKeyId;
                _secretAccessKey = secretAccessKey;
                _region = region;
                _service = service;
            }

            public async Task SignRequest(HttpRequestMessage request)
            {
                var timestamp = DateTime.UtcNow;
                var amzDate = timestamp.ToString("yyyyMMddTHHmmssZ");
                var dateStamp = timestamp.ToString("yyyyMMdd");

                request.Headers.Host = request.RequestUri?.Host;
                request.Headers.Add("X-Amz-Date", amzDate);

                var canonicalRequest = await CreateCanonicalRequest(request);
                var stringToSign = CreateStringToSign(dateStamp, canonicalRequest);
                var signature = CreateSignature(dateStamp, stringToSign);
                var authorizationHeader = CreateAuthorizationHeader(dateStamp, signature);

                request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
            }

            private async Task<string> CreateCanonicalRequest(HttpRequestMessage request)
            {
                var sb = new StringBuilder();
                sb.Append(request.Method + "\n");
                sb.Append(request.RequestUri?.AbsolutePath + "\n");
                sb.Append(request.RequestUri?.Query.TrimStart('?') + "\n");

                var sortedHeaders = request.Headers.OrderBy(h => h.Key.ToLowerInvariant(), StringComparer.Ordinal)
                    .ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
                foreach (var header in sortedHeaders)
                {
                    sb.Append($"{header.Key}:{header.Value.Trim()}\n");
                }
                sb.Append("\n");

                var signedHeaders = string.Join(";", sortedHeaders.Keys);
                sb.Append(signedHeaders + "\n");

                var payload = request.Content != null ? await request.Content.ReadAsByteArrayAsync() : Array.Empty<byte>();
                sb.Append(ToHexString(SHA256.HashData(payload)).ToLowerInvariant());

                return sb.ToString();
            }

            private string CreateStringToSign(string dateStamp, string canonicalRequest)
            {
                var sb = new StringBuilder();
                sb.Append("AWS4-HMAC-SHA256\n");
                sb.Append(dateStamp + "T000000Z\n"); // Simplified, should be amzDate
                sb.Append($"{dateStamp}/{_region}/{_service}/aws4_request\n");
                sb.Append(ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))).ToLowerInvariant());
                return sb.ToString();
            }

            private byte[] CreateSignature(string dateStamp, string stringToSign)
            {
                byte[] kSecret = Encoding.UTF8.GetBytes("AWS4" + _secretAccessKey);
                byte[] kDate = HmacSha256(kSecret, dateStamp);
                byte[] kRegion = HmacSha256(kDate, _region);
                byte[] kService = HmacSha256(kRegion, _service);
                byte[] kSigning = HmacSha256(kService, "aws4_request");
                return HmacSha256(kSigning, stringToSign);
            }

            private string CreateAuthorizationHeader(string dateStamp, byte[] signature)
            {
                var sortedHeaders = "host;x-amz-date;x-amz-target"; // Simplified
                var sb = new StringBuilder();
                sb.Append($"AWS4-HMAC-SHA256 Credential={_accessKeyId}/{dateStamp}/{_region}/{_service}/aws4_request, ");
                sb.Append($"SignedHeaders={sortedHeaders}, ");
                sb.Append($"Signature={ToHexString(signature).ToLowerInvariant()}");
                return sb.ToString();
            }

            private static byte[] HmacSha256(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
            private static string ToHexString(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");
        }

        // --- HASHICORP IMPLEMENTATION ---
        private async Task<string?> GetHashiCorpVaultSecretAsync(string secretName, KeyVaultConfig config)
        {
            var client = _httpClientFactory.CreateClient("HashiCorpClient");
            var vaultUrl = config.Url.TrimEnd('/');
            var secretUrl = $"{vaultUrl}/v1/secret/data/{secretName}";

            var request = new HttpRequestMessage(HttpMethod.Get, secretUrl);
            request.Headers.Add("X-Vault-Token", config.HashiCorpToken);

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"HashiCorp Vault API error ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}", null);
                return null;
            }
            var responseJson = await response.Content.ReadFromJsonAsync<JsonDocument>();
            return responseJson?.RootElement.GetProperty("data").GetProperty("data").GetProperty("value").GetString();
        }

        // --- GCP IMPLEMENTATION ---
        private async Task<string?> GetGcpSecretManagerSecretAsync(string secretName, KeyVaultConfig config)
        {
            var client = _httpClientFactory.CreateClient("GcpClient");
            if (string.IsNullOrEmpty(config.GcpServiceAccountKeyJson))
            {
                _logger.LogWarning("Missing GCP service account key (TOKENS_GCP_SERVICE_ACCOUNT_KEY_JSON).");
                return null;
            }
            var keyData = JsonSerializer.Deserialize<GcpServiceAccountKey>(config.GcpServiceAccountKeyJson);
            if (keyData == null)
            {
                _logger.LogError("Failed to parse GCP Service Account Key JSON.", null);
                return null;
            }

            var accessToken = await GetGcpAccessTokenAsync(keyData);
            if (string.IsNullOrEmpty(accessToken)) return null;

            var secretUrl = $"https://secretmanager.googleapis.com/v1/projects/{keyData.ProjectId}/secrets/{secretName}/versions/latest:access";

            var request = new HttpRequestMessage(HttpMethod.Get, secretUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"GCP Secret Manager API error ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}", null);
                return null;
            }
            var responseJson = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var base64Payload = responseJson?.RootElement.GetProperty("payload").GetProperty("data").GetString();
            return string.IsNullOrEmpty(base64Payload) ? null : Encoding.UTF8.GetString(Convert.FromBase64String(base64Payload));
        }

        private async Task<string?> GetGcpAccessTokenAsync(GcpServiceAccountKey keyData)
        {
            var client = _httpClientFactory.CreateClient("GcpAuthClient");
            var now = DateTimeOffset.UtcNow;
            var claims = new GcpJwtClaims
            {
                Issuer = keyData.ClientEmail,
                Scope = "https://www.googleapis.com/auth/cloud-platform",
                Audience = keyData.TokenUri,
                IssuedAt = now.ToUnixTimeSeconds(),
                ExpiresAt = now.AddHours(1).ToUnixTimeSeconds()
            };
            var header = new { alg = "RS256", typ = "JWT" };
            var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
            var encodedClaims = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims));
            var signaturePayload = $"{encodedHeader}.{encodedClaims}";
            var signature = SignData(signaturePayload, keyData.PrivateKey);
            var jwt = $"{signaturePayload}.{Base64UrlEncode(signature)}";

            var tokenRequestContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new KeyValuePair<string, string>("assertion", jwt)
            });

            var response = await client.PostAsync(keyData.TokenUri, tokenRequestContent);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"GCP access token request failed ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}", null);
                return null;
            }
            var responseJson = await response.Content.ReadFromJsonAsync<JsonDocument>();
            return responseJson?.RootElement.GetProperty("access_token").GetString();
        }

        private static byte[] SignData(string payload, string privateKey)
        {
            var keyBytes = Convert.FromBase64String(privateKey.Replace("-----BEGIN PRIVATE KEY-----", "").Replace("-----END PRIVATE KEY-----", "").Replace("\n", ""));
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(keyBytes, out _);
            return rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        private static string Base64UrlEncode(byte[] input) => Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Internal Models
        private class GcpServiceAccountKey { [JsonPropertyName("project_id")] public string ProjectId { get; set; } = ""; [JsonPropertyName("private_key")] public string PrivateKey { get; set; } = ""; [JsonPropertyName("client_email")] public string ClientEmail { get; set; } = ""; [JsonPropertyName("token_uri")] public string TokenUri { get; set; } = ""; }
        private class GcpJwtClaims { [JsonPropertyName("iss")] public string Issuer { get; set; } = ""; [JsonPropertyName("scope")] public string Scope { get; set; } = ""; [JsonPropertyName("aud")] public string Audience { get; set; } = ""; [JsonPropertyName("iat")] public long IssuedAt { get; set; } [JsonPropertyName("exp")] public long ExpiresAt { get; set; } }

        #endregion
    }
}