using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using x3squaredcircles.DesignToken.Generator.Models;
using DesignTokenModel = x3squaredcircles.DesignToken.Generator.Models.DesignToken;

namespace x3squaredcircles.DesignToken.Generator.Services
{
    public class AdobeXdConnectorService : IAdobeXdConnectorService
    {
        private readonly HttpClient _httpClient;
        private readonly IAppLogger _logger;

        public AdobeXdConnectorService(HttpClient httpClient, IAppLogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<TokenCollection> ExtractTokensAsync(TokensConfiguration config)
        {
            _logger.LogStartPhase("Token Extraction (Adobe XD)");
            var apiToken = Environment.GetEnvironmentVariable("ADOBE_XD_API_TOKEN");
            var apiKey = Environment.GetEnvironmentVariable("ADOBE_API_KEY"); // Adobe IO requires an API key (Client ID)

            if (string.IsNullOrEmpty(apiToken) || string.IsNullOrEmpty(apiKey))
                throw new DesignTokenException(DesignTokenExitCode.TokenExtractionFailure, "Adobe API token or API key not found in environment.");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiToken}");
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);

            try
            {
                var documentId = ExtractDocumentIdFromUrl(config.AdobeXd.ProjectUrl);
                if (string.IsNullOrEmpty(documentId))
                    throw new DesignTokenException(DesignTokenExitCode.InvalidConfiguration, "Invalid Adobe XD Share URL format.");

                var rendition = await GetDocumentRenditionAsync(documentId);
                var tokens = ParseRenditionForTokens(rendition);

                _logger.LogInfo($"✓ Extracted {tokens.Count} tokens from Adobe XD document rendition.");
                _logger.LogEndPhase("Token Extraction (Adobe XD)", true);

                return new TokenCollection
                {
                    Name = $"Adobe XD Tokens - {documentId}",
                    Source = "adobe-xd",
                    Tokens = tokens,
                    Metadata = new Dictionary<string, object> { ["documentId"] = documentId }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("An unexpected error occurred while extracting tokens from Adobe XD.", ex);
                throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, $"Adobe XD token extraction failed: {ex.Message}", ex);
            }
        }

        private async Task<AdobeXdRendition> GetDocumentRenditionAsync(string documentId)
        {
            var url = $"https://cc-api-storage.adobe.io/api/v1/links/{documentId}";
            var response = await _httpClient.GetFromJsonAsync<AdobeXdLinkResponse>(url);

            var renditionUrl = response?.Link?.RenditionHref;
            if (string.IsNullOrEmpty(renditionUrl))
                throw new DesignTokenException(DesignTokenExitCode.DesignPlatformApiFailure, "Could not resolve rendition URL from Adobe XD share link.");

            return await _httpClient.GetFromJsonAsync<AdobeXdRendition>(renditionUrl);
        }

        private List<DesignTokenModel> ParseRenditionForTokens(AdobeXdRendition rendition)
        {
            var tokens = new List<DesignTokenModel>();
            if (rendition?.Resources?.CharacterStyles == null) return tokens;

            foreach (var style in rendition.Resources.CharacterStyles)
            {
                tokens.Add(new DesignTokenModel
                {
                    Name = style.Name,
                    Type = "typography",
                    Category = "typography",
                    Value = new
                    {
                        fontFamily = style.FontFamily,
                        fontSize = $"{style.FontSize}px",
                        fontWeight = 400 // Simplified
                    }
                });
            }
            // A full implementation would parse artboards for colors, etc.
            return tokens;
        }

        private string ExtractDocumentIdFromUrl(string url)
        {
            try { return new Uri(url).Segments.LastOrDefault()?.TrimEnd('/'); }
            catch { return string.Empty; }
        }

        #region Adobe XD API DTOs
        private class AdobeXdLinkResponse { [JsonPropertyName("link")] public AdobeXdLink? Link { get; set; } }
        private class AdobeXdLink { [JsonPropertyName("meta.rendition.href")] public string? RenditionHref { get; set; } }
        private class AdobeXdRendition { [JsonPropertyName("resources")] public AdobeXdResources? Resources { get; set; } }
        private class AdobeXdResources { [JsonPropertyName("characterStyles")] public List<AdobeXdCharacterStyle>? CharacterStyles { get; set; } }
        private class AdobeXdCharacterStyle { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("font-family")] public string FontFamily { get; set; } = ""; [JsonPropertyName("font-size")] public int FontSize { get; set; } }
        #endregion
    }
}